using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic; 
using System.Threading; 
using System.Threading.Tasks; 
using Application.Services.AI.Interfaces;
using Application.Services.AI.PayloadBuilders;

namespace Application.Services.AI.Builders; 

public class DeepSeekPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public DeepSeekPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<DeepSeekPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public async Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<object>? tools = null, 
        CancellationToken cancellationToken = default) 
    {
        // Create the base request object
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        // Set required model properties
        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        // Apply parameters using simplified method with thinking mode support
        AddParameters(requestObj, context);

        // Process messages with system prompt
        var processedMessages = await ProcessMessagesForDeepSeekAsync(context, cancellationToken);
        requestObj["messages"] = processedMessages;

        // Add tools only if not in thinking mode
        if ( tools?.Any() == true)
        {
            requestObj["tools"] = tools;
            requestObj["tool_choice"] = "auto";
            Logger?.LogInformation("Adding {ToolCount} tool definitions to DeepSeek payload for model {ModelCode}",
                tools.Count, model.ModelCode);
        }

        // Apply DeepSeek-specific thinking parameters
        CustomizePayload(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private async Task<List<object>> ProcessMessagesForDeepSeekAsync(AiRequestContext context,
        CancellationToken cancellationToken) 
    {
        var processedMessages = new List<object>();
        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ?? context.UserSettings?.ModelParameters.SystemInstructions;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
        }
        
        var mergedHistory = MergeConsecutiveRoles(
            context.History.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = _multimodalContentParser.Parse(rawContent);
            var processedParts = new List<string>();
            
            foreach (var part in contentParts)
            {
                string partText = "";
                
                if (part is TextPart tp)
                {
                    partText = tp.Text;
                }
                else if (part is ImagePart ip)
                {
                    partText = $"[Image: {ip.FileName ?? ip.MimeType} - Not Sent]";
                }
                else if (part is FilePart fp)
                {
                    if (fp.MimeType == "text/csv" || fp.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Logger?.LogWarning("CSV file {FileName} detected - DeepSeek doesn't support CSV files directly. Using the csv_reader plugin is recommended instead.", fp.FileName);
                        partText = $"Note: The CSV file '{fp.FileName}' can't be processed directly by DeepSeek. " + 
                                  $"Please use the csv_reader tool to analyze this file. Example usage:\n" + 
                                  $"csv_reader(file_name=\"{fp.FileName}\", max_rows=100, analyze=true)";
                    }
                    else
                    {
                        partText = $"[File: {fp.FileName} ({fp.MimeType}) - Not Sent]";
                    }
                }
                
                if (!string.IsNullOrEmpty(partText))
                {
                    processedParts.Add(partText);
                }
            }
            
            string contentText = string.Join("\n", processedParts).Trim();

            if (!string.IsNullOrWhiteSpace(contentText))
            {
                processedMessages.Add(new { role, content = contentText });
            }
        }

        bool isReasonerModel = context.SpecificModel.ModelCode?.ToLower().Contains("reasoner") ?? false;
        if (isReasonerModel && processedMessages.Count > 0)
        {
            int firstNonSystemIndex = processedMessages.FindIndex(m => GetRoleFromDynamicMessage(m) != "system");
            if (firstNonSystemIndex == -1 ||
                GetRoleFromDynamicMessage(processedMessages[firstNonSystemIndex]) != "user")
            {
                int insertIndex = firstNonSystemIndex == -1 ? processedMessages.Count : firstNonSystemIndex;
                processedMessages.Insert(insertIndex,
                    new { role = "user", content = "Proceed." }); 
                Logger?.LogWarning(
                    "Inserted placeholder user message for DeepSeek reasoner model {ModelCode} as the first non-system message was not 'user'.",
                    context.SpecificModel.ModelCode);
            }
        }


        await Task.CompletedTask;
        return processedMessages;
    }

    /// <summary>
    /// Customize the payload with DeepSeek-specific parameters, particularly for thinking mode
    /// </summary>
    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        
        if (useThinking)
        {
            if (!requestObj.ContainsKey("enable_cot"))
            {
                requestObj["enable_cot"] = true;
                Logger?.LogDebug("Enabled DeepSeek 'enable_cot' parameter for model {ModelCode}", 
                    context.SpecificModel.ModelCode);
            }

            if (!requestObj.ContainsKey("enable_reasoning"))
            {
                requestObj["enable_reasoning"] = true;
                Logger?.LogDebug("Enabled DeepSeek 'enable_reasoning' parameter for model {ModelCode}", 
                    context.SpecificModel.ModelCode);
            }
            
            if (!requestObj.ContainsKey("reasoning_mode"))
            {
                requestObj["reasoning_mode"] = "detailed";
                Logger?.LogDebug("Set DeepSeek 'reasoning_mode' to 'detailed' for thinking");
            }
        }
    }
}