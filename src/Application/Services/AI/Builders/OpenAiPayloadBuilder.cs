using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.Interfaces;
using Application.Services.AI.PayloadBuilders;

namespace Application.Services.AI.Builders;

public class OpenAiPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public OpenAiPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<OpenAiPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<object>? tools = null, CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        // Set base model information
        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;
        
        AddParameters(requestObj, context, context.IsThinking);

        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ??
                               context.UserSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            requestObj["instructions"] = systemMessage.Trim();
            Logger?.LogDebug("Adding system instructions for model {ModelCode}", model.ModelCode);
        }

        var processedMessages = ProcessMessagesForOpenAIInput(context.History);
        requestObj["input"] = processedMessages;

        if ( tools?.Any() == true)
        {
            Logger?.LogInformation("Adding {ToolCount} tool definitions to OpenAI payload for model {ModelCode}",
                tools.Count, model.ModelCode);
            requestObj["tools"] = tools;
            requestObj["tool_choice"] = "auto";
        }

        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
        requestObj.Remove("stop");

        CustomizePayload(requestObj, context);

        return Task.FromResult(new AiRequestPayload(requestObj));
    }

    private List<object> ProcessMessagesForOpenAIInput(List<MessageDto> history)
    {
        var processedMessages = new List<object>();


        foreach (var message in history)
        {
            var role = message.IsFromAi ? "assistant" : "user";
            var rawContent = message.Content?.Trim() ?? "";

            if (string.IsNullOrEmpty(rawContent)) continue;

            if (role == "user")
            {
                var contentParts = _multimodalContentParser.Parse(rawContent);
                var openAiContentItems = new List<object>();
                bool hasNonTextContent = false;

                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            openAiContentItems.Add(new { type = "input_text", text = textPart.Text });
                            break;
                        case ImagePart imagePart:
                            openAiContentItems.Add(new
                            {
                                type = "input_image",
                                image_url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}"
                            });
                            hasNonTextContent = true;
                            break;
                        case FilePart filePart:
                            // Check if the file is a CSV, which OpenAI doesn't support directly
                            if (filePart.MimeType == "text/csv" || 
                                filePart.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                            {
                                // Log that CSV files should be handled by our plugin instead of direct upload
                                Logger?.LogWarning("CSV file {FileName} detected - OpenAI doesn't support CSV files directly. " +
                                    "Using the csv_reader plugin is recommended instead.", filePart.FileName);
                                
                                // Add a text content explaining why the file can't be used directly
                                openAiContentItems.Add(new
                                {
                                    type = "input_text",
                                    text = $"Note: The CSV file '{filePart.FileName}' can't be processed directly by OpenAI. " +
                                           $"Please use the csv_reader tool to analyze this file. Example usage:\n\n" +
                                           $"```json\n{{\n  \"type\": \"function\",\n  \"function\": {{\n    \"name\": \"csv_reader\",\n    \"arguments\": {{\n      \"file_name\": \"{filePart.FileName}\",\n      \"max_rows\": 100,\n      \"analyze\": true\n    }}\n  }}\n}}\n```"
                                });
                            }
                            else
                            {
                                Logger?.LogInformation("Adding file {FileName} using 'input_file' type.",
                                    filePart.FileName);
                                openAiContentItems.Add(new
                                {
                                    type = "input_file",
                                    filename = filePart.FileName,
                                    file_data = $"data:{filePart.MimeType};base64,{filePart.Base64Data}"
                                });
                                hasNonTextContent = true;
                            }
                            break;
                    }
                }

                if (openAiContentItems.Any())
                {
                    if (openAiContentItems.Count == 1 && !hasNonTextContent && openAiContentItems[0] is var textItem &&
                        textItem.GetType().GetProperty("type")?.GetValue(textItem)?.ToString() == "input_text")
                    {
                        string? textContent = textItem.GetType().GetProperty("text")?.GetValue(textItem)?.ToString();
                        if (!string.IsNullOrEmpty(textContent))
                        {
                            processedMessages.Add(new { role = "user", content = textContent });
                        }
                    }
                    else if (openAiContentItems.Count > 0)
                    {
                        processedMessages.Add(new { role = "user", content = openAiContentItems.ToArray() });
                    }
                }
            }
            else
            {
                processedMessages.Add(new { role = "assistant", content = rawContent });
            }
        }

        return processedMessages;
    }
    
    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking == true || 
                                   context.SpecificModel.SupportsThinking;

        if (useEffectiveThinking)
        {
            requestObj["reasoning"] = new { effort = "medium", summary = "detailed" };
            Logger?.LogDebug("Adding reasoning effort for OpenAI model {ModelCode}", context.SpecificModel.ModelCode);

            requestObj.Remove("temperature");
            requestObj.Remove("top_p");
            requestObj.Remove("max_output_tokens");
            requestObj.Remove("max_tokens");

            Logger?.LogDebug("Removed potentially conflicting parameters due to reasoning effort.");
        }
        
        if (requestObj.TryGetValue("max_tokens", out var maxTokensValue) && !requestObj.ContainsKey("reasoning"))
        {
            requestObj.Remove("max_tokens");
            if (!requestObj.ContainsKey("max_output_tokens"))
            {
                requestObj["max_output_tokens"] = maxTokensValue;
                Logger?.LogDebug("Mapped 'max_tokens' to 'max_output_tokens'.");
            }
        }
    }
}