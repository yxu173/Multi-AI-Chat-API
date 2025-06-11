using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.Interfaces;

namespace Application.Services.AI.Builders;

public class AnthropicPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public AnthropicPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<AnthropicPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public async Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<PluginDefinition>? tools = null, CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        AddParameters(requestObj, context);

        var (systemPrompt, processedMessages) = await ProcessMessagesForAnthropicAsync(context.History, context, cancellationToken);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            requestObj["system"] = systemPrompt;
        }
        requestObj["messages"] = processedMessages;

        if ( tools?.Any() == true) 
        {
            Logger?.LogInformation("Adding {ToolCount} tool definitions to Anthropic payload for model {ModelCode}",
                tools.Count, model.ModelCode);
            
            var formattedTools = tools.Select(def => new
            {
                name = def.Name,
                description = def.Description,
                input_schema = def.ParametersSchema
            }).ToList();
            
            requestObj["tools"] = formattedTools;
            requestObj["tool_choice"] = new { type = "auto" };
        }

        CustomizePayload(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private async Task<(string? SystemPrompt, List<object> Messages)> ProcessMessagesForAnthropicAsync(List<MessageDto> history, AiRequestContext context, CancellationToken cancellationToken)
    {
        string? agentSystemMessage = context.AiAgent?.ModelParameter.SystemInstructions;
        string? userSystemMessage = context.UserSettings?.ModelParameters.SystemInstructions;
        string? finalSystemPrompt = agentSystemMessage ?? userSystemMessage;


        var otherMessages = new List<object>();
        var mergedHistory = MergeConsecutiveRoles( 
            history.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            string anthropicRole = role; 
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
            if (contentParts.Count > 1 || contentParts.Any(p => p is not TextPart))
            {
                var anthropicContentItems = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart tp: anthropicContentItems.Add(new { type = "text", text = tp.Text }); break;
                        case ImagePart ip:
                            if (Validators.IsValidAnthropicImageType(ip.MimeType, out var mediaType)) 
                            {
                                anthropicContentItems.Add(new
                                {
                                    type = "image",
                                    source = new { type = "base64", media_type = mediaType, data = ip.Base64Data }
                                });
                            }
                            else
                            {
                                Logger?.LogWarning("Unsupported image type '{MimeType}' for Anthropic. Sending placeholder text.", ip.MimeType);
                                anthropicContentItems.Add(new { type = "text", text = $"[Image: {ip.FileName ?? ip.MimeType} - Unsupported Type]" });
                            }
                            break;
                        case FilePart fp:
                            // Special handling for CSV files
                            if (fp.MimeType == "text/csv" || fp.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                Logger?.LogWarning("CSV file {FileName} detected - Anthropic doesn't support CSV files directly. " +
                                    "Using the csv_reader plugin is recommended instead.", fp.FileName);
                                
                                anthropicContentItems.Add(new { 
                                    type = "text", 
                                    text = $"Note: The CSV file '{fp.FileName}' can't be processed directly by Anthropic. " +
                                           $"Please use the csv_reader tool to analyze this file. Example usage:\n\n" +
                                           $"{{\n  \"name\": \"csv_reader\",\n  \"input\": {{\n    \"file_name\": \"{fp.FileName}\",\n    \"max_rows\": 100,\n    \"analyze\": true\n  }}\n}}" 
                                });
                            }
                            else if (Validators.IsValidAnthropicDocumentType(fp.MimeType, out var docMediaType))
                            {
                                Logger?.LogInformation("Adding document {FileName} ({MediaType}) to Anthropic message using 'document' type.", fp.FileName, docMediaType);
                                var mediaTypeValue = docMediaType;
                                var dataValue = fp.Base64Data;
                                anthropicContentItems.Add(new
                                {
                                    type = "document",
                                    source = new { type = "base64", media_type = mediaTypeValue, data = dataValue }
                                });
                            }
                            else
                            {
                                Logger?.LogWarning("Document type '{MimeType}' is not listed as supported by Anthropic. Sending placeholder text for file {FileName}.", fp.MimeType, fp.FileName);
                                anthropicContentItems.Add(new { type = "text", text = $"[Document Attached: {fp.FileName} - Unsupported Type ({fp.MimeType}) for direct API processing]" });
                            }
                            break;
                    }
                }
                if (anthropicContentItems.Any()) otherMessages.Add(new { role = anthropicRole, content = anthropicContentItems.ToArray() });
            }
            else if (contentParts.Count == 1 && contentParts[0] is TextPart singleTextPart)
            {
                otherMessages.Add(new { role = anthropicRole, content = singleTextPart.Text });
            }
        }

        EnsureAlternatingRoles(otherMessages, "user", "assistant"); 
        return (finalSystemPrompt?.Trim(), otherMessages);
    }

    protected override void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        // Anthropic-specific logic: handle max_tokens mapping and thinking mode
        if (!requestObj.ContainsKey("max_tokens"))
        {
            if (requestObj.TryGetValue("max_output_tokens", out var maxOutputTokens))
            {
                requestObj["max_tokens"] = maxOutputTokens;
                Logger?.LogDebug("Mapped 'max_output_tokens' to 'max_tokens' for Anthropic");
            }
        }
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        if (useThinking)
        {
            const int defaultThinkingBudget = 1024;
            requestObj["thinking"] = new { type = "enabled", budget_tokens = defaultThinkingBudget };
            
            requestObj["temperature"] = 1.0;
            requestObj.Remove("top_k");
            requestObj.Remove("top_p");
            Logger?.LogDebug("Enabled Anthropic native 'thinking' parameter with budget {Budget}", defaultThinkingBudget);
        }
        
        if (!requestObj.ContainsKey("anthropic_version"))
        {
            requestObj["anthropic_version"] = "2023-06-01";
        }
    }
} 