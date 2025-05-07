using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.PayloadBuilders;

public class AnthropicPayloadBuilder : BasePayloadBuilder, IAnthropicPayloadBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public AnthropicPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<AnthropicPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        var parameters = GetMergedParameters(context);
        ApplyParametersToRequest(requestObj, parameters, model.ModelType); 

        var (systemPrompt, processedMessages) = ProcessMessagesForAnthropic(context.History, context);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            requestObj["system"] = systemPrompt;
        }
        requestObj["messages"] = processedMessages;

        if (toolDefinitions?.Any() == true && IsParameterSupported("tools", model.ModelType)) 
        {
             Logger?.LogInformation("Adding {ToolCount} tool definitions to Anthropic payload for model {ModelCode}",
                toolDefinitions.Count, model.ModelCode);
            requestObj["tools"] = toolDefinitions;
            if (IsParameterSupported("tool_choice", model.ModelType)) 
            {
                requestObj["tool_choice"] = new { type = "auto" };
            }
        }

        AddAnthropicSpecificParameters(requestObj, context);

        if (!requestObj.ContainsKey("max_tokens"))
        {
            const int defaultMaxTokens = 4096; 
            requestObj["max_tokens"] = defaultMaxTokens;
            Logger?.LogWarning("Anthropic request payload was missing 'max_tokens'. Added default value: {DefaultMaxTokens}", defaultMaxTokens);
        }

        return new AiRequestPayload(requestObj);
    }

    private (string? SystemPrompt, List<object> Messages) ProcessMessagesForAnthropic(List<MessageDto> history, AiRequestContext context)
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

            var contentParts = _multimodalContentParser.Parse(rawContent);
            if (contentParts.Count > 1 || contentParts.Any(p => p is not TextPart))
            {
                var anthropicContentItems = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart tp: anthropicContentItems.Add(new { type = "text", text = tp.Text }); break;
                        case ImagePart ip:
                            if (IsValidAnthropicImageType(ip.MimeType, out var mediaType)) 
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
                            else if (IsValidAnthropicDocumentType(fp.MimeType, out var docMediaType))
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

    private void AddAnthropicSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        if (useEffectiveThinking && !requestObj.ContainsKey("thinking"))
        {
            if (IsParameterSupported("thinking", ModelType.Anthropic))
            {
                 const int defaultThinkingBudget = 1024; 
                 requestObj["thinking"] = new { type = "enabled", budget_tokens = defaultThinkingBudget };
                 requestObj["temperature"] = 1.0;
                 requestObj.Remove("top_k");
                 requestObj.Remove("top_p");
                 Logger?.LogDebug("Enabled Anthropic 'thinking' parameter with budget {Budget} (Effective: {UseThinking})", defaultThinkingBudget, useEffectiveThinking);
            }
            else
            {
                Logger?.LogWarning("Requested thinking for Anthropic model {ModelCode}, but 'thinking' parameter is not marked as supported.", context.SpecificModel.ModelCode);
            }
        }
    }
} 