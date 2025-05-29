using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.Interfaces;

namespace Application.Services.AI.Builders;

public class GrokPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public GrokPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<GrokPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public async Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<object>? tools = null, CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        AddParameters(requestObj, context);
        
        CustomizePayload(requestObj, context);

        var processedMessages = await ProcessMessagesForGrokInputAsync(context.History, context.AiAgent, context.UserSettings, cancellationToken);
        requestObj["messages"] = processedMessages;

        if ( tools != null && tools.Any())
        {
            requestObj["tools"] = tools;

            if (!string.IsNullOrEmpty(context.FunctionCall))
            {
                if (context.FunctionCall == "auto")
                {
                    requestObj["tool_choice"] = "auto";
                }
                else if (context.FunctionCall == "none")
                {
                    requestObj["tool_choice"] = "none";
                }
                else
                {
                    requestObj["tool_choice"] = new
                    {
                        type = "function",
                        function = new
                        {
                            name = context.FunctionCall
                        }
                    };
                }
            }
        }

        return new AiRequestPayload(requestObj);
    }


    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        requestObj["temperature"] = 0.0;
        
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        
        if (useThinking)
        {
            requestObj["reasoning_effort"] = "high";
            Logger?.LogDebug("Set Grok 'reasoning_effort' to 'maximum' for thinking mode");
        }
    }

    private object[] PrepareFunctionDefinitions(List<FunctionDefinitionDto> functions)
    {
        var tools = new List<object>();

        foreach (var function in functions)
        {
            var functionDef = new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = function.Name,
                    ["description"] = function.Description ?? string.Empty
                }
            };

            if (function.Parameters != null)
            {
                ((Dictionary<string, object>)functionDef["function"])["parameters"] = function.Parameters;
            }

            tools.Add(functionDef);
        }

        return tools.ToArray();
    }

    private async Task<List<object>> ProcessMessagesForGrokInputAsync(
        List<MessageDto> history,
        AiAgent? aiAgent,
        UserAiModelSettings? userSettings,
        CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();

        string? systemMessage = aiAgent?.ModelParameter.SystemInstructions ??
                                userSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
            Logger?.LogDebug("Adding system instructions as a message for Grok.");
        }

        foreach (var message in history)
        {
            var role = message.IsFromAi ? "system" : "user";
            var rawContent = message.Content?.Trim() ?? "";

            if (string.IsNullOrEmpty(rawContent)) continue;

            if (!message.IsFromAi)
            {
                var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
                var contentArray = new List<object>();

                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            var txt = textPart.Text?.Trim();
                            if (!string.IsNullOrEmpty(txt))
                                contentArray.Add(new { type = "text", text = txt });
                            break;
                        case ImagePart imagePart:
                            contentArray.Add(new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}",
                                    detail = "high"
                                }
                            });
                            break;
                        case FilePart filePart:
                            if (filePart.MimeType == "text/csv" || filePart.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                Logger?.LogWarning("CSV file {FileName} detected - Grok doesn't support CSV files directly. Using the csv_reader plugin is recommended instead.", filePart.FileName);

                                contentArray.Add(new { type = "text", text = $"Note: The CSV file '{filePart.FileName}' can't be processed directly by Grok. Please use the csv_reader tool to analyze this file. Example usage:\n\n{{\n  \"name\": \"csv_reader\",\n  \"arguments\": {{\n    \"file_name\": \"{filePart.FileName}\",\n    \"max_rows\": 100,\n    \"analyze\": true\n  }}\n}}" });
                            }
                            else
                            {
                                contentArray.Add(new { type = "text", text = $"[Attached file: {filePart.FileName} ({filePart.MimeType}) - Not supported by Grok directly]" });
                            }
                            break;
                    }
                }

                if (contentArray.Count > 0)
                {
                    processedMessages.Add(new { role = role, content = contentArray });
                }
            }
            else
            {
                if (message.FunctionCall != null)
                {
                    processedMessages.Add(new
                    {
                        role = "assistant",
                        tool_calls = new[]
                        {
                            new
                            {
                                type = "function",
                                function = new
                                {
                                    name = message.FunctionCall.Name,
                                    arguments = message.FunctionCall.Arguments
                                }
                            }
                        }
                    });
                }
                else if (message.FunctionResponse != null)
                {
                    processedMessages.Add(new
                    {
                        role = "tool",
                        tool_call_id = message.FunctionResponse.FunctionCallId,
                        name = message.FunctionResponse.Name,
                        content = message.FunctionResponse.Content
                    });
                }
                else
                {
                    processedMessages.Add(new { role = "assistant", content = rawContent });
                }
            }
        }

        return processedMessages;
    }
}