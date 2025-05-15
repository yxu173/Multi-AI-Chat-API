using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Microsoft.Extensions.Logging;
using Application.Services.AI.Interfaces;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.PayloadBuilders;

namespace Application.Services.AI.Builders;

public class QwenPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public QwenPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<QwenPayloadBuilder> logger)
        : base(logger)
    {
        _multimodalContentParser =
            multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<object>? tools = null, CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;
        requestObj["stream_options"] = new { include_usage = true };
        
        AddParameters(requestObj, context, context.IsThinking);
        
        CustomizePayload(requestObj, context);

        var processedMessages = ProcessMessagesForQwenInput(context.History, context.AiAgent, context.UserSettings);
        requestObj["messages"] = processedMessages;

        if (!context.IsThinking && tools != null && tools.Any())
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

        return Task.FromResult(new AiRequestPayload(requestObj));
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

    private List<object> ProcessMessagesForQwenInput(
        List<MessageDto> history,
        AiAgent? aiAgent,
        UserAiModelSettings? userSettings)
    {
        var processedMessages = new List<object>();

        string? systemMessage = aiAgent?.ModelParameter.SystemInstructions ??
                                userSettings?.ModelParameters.SystemInstructions;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
            Logger?.LogDebug("Added system instructions as a message for Qwen.");
        }

        foreach (var message in history)
        {
            string role = message.IsFromAi ? "assistant" : "user";

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
                            },
                            id = message.FunctionCall.Id
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
                string rawContent = message.Content?.Trim() ?? "";
                if (string.IsNullOrEmpty(rawContent)) continue;

                if (!message.IsFromAi)
                {
                    var contentParts = _multimodalContentParser.Parse(rawContent);
                    bool hasMultimodalContent = contentParts.Any(p => p is ImagePart || p is FilePart);

                    if (hasMultimodalContent)
                    {
                        var contentArray = new List<object>();

                        foreach (var part in contentParts)
                        {
                            switch (part)
                            {
                                case TextPart textPart:
                                    var txt = textPart.Text?.Trim();
                                    if (!string.IsNullOrEmpty(txt))
                                        contentArray.Add(new
                                        {
                                            type = "text",
                                            text = txt
                                        });
                                    break;
                                case ImagePart imagePart:
                                    contentArray.Add(new
                                    {
                                        type = "image_url",
                                        image_url = new
                                        {
                                            url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}"
                                        }
                                    });
                                    break;
                                case FilePart filePart:
                                    if (filePart.MimeType == "text/csv" || filePart.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        Logger?.LogWarning("CSV file {FileName} detected - Qwen doesn't support CSV files directly. " +
                                            "Using the csv_reader plugin is recommended instead.", filePart.FileName);
                                        
                                        contentArray.Add(new
                                        {
                                            type = "text",
                                            text = $"Note: The CSV file '{filePart.FileName}' can't be processed directly by Qwen. " +
                                                   $"Please use the csv_reader tool to analyze this file. Example usage:\n\n" +
                                                   $"{{\n  \"type\": \"function\",\n  \"function\": {{\n    \"name\": \"csv_reader\",\n    \"arguments\": {{\n      \"file_name\": \"{filePart.FileName}\",\n      \"max_rows\": 100,\n      \"analyze\": true\n    }}\n  }}\n}}"
                                        });
                                    }
                                    else
                                    {
                                        contentArray.Add(new
                                        {
                                            type = "text",
                                            text = $"[Attached file: {filePart.FileName}]"
                                        });
                                    }
                                    break;
                            }
                        }

                        if (contentArray.Count > 0)
                        {
                            processedMessages.Add(new
                            {
                                role = role,
                                content = contentArray
                            });
                        }
                    }
                    else
                    {
                        processedMessages.Add(new { role = role, content = rawContent });
                    }
                }
                else
                {
                    processedMessages.Add(new { role = role, content = rawContent });
                }
            }
        }

        return processedMessages;
    }
    
    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        
        requestObj["enable_thinking"] = useThinking;
        
        if (useThinking)
        {
            Logger?.LogDebug("Enabled Qwen native 'enable_thinking' parameter for model {ModelCode}", 
                context.SpecificModel.ModelCode);
            
            if (requestObj.ContainsKey("temperature") && requestObj["temperature"] is double temp && temp < 0.7)
            {
                requestObj["temperature"] = 0.7;
                Logger?.LogDebug("Increased temperature for thinking mode");
            }
        }
    }
}