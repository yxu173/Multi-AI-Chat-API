using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.PayloadBuilders;

public class QwenPayloadBuilder : BasePayloadBuilder, IQwenPayloadBuilder
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

    public AiRequestPayload PreparePayload(AiRequestContext context)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;
        requestObj["stream_options"] = new { include_usage = true }; // Include usage in the stream response
        requestObj["enable_thinking"] = true; // Enable thinking for the model

        var parameters = GetMergedParameters(context);
        foreach (var param in parameters)
        {
            if (IsParameterSupported(param.Key, model.ModelType))
            {
                requestObj[param.Key] = param.Value;
            }
        }

        var processedMessages = ProcessMessagesForQwenInput(context.History, context.AiAgent, context.UserSettings);
        requestObj["messages"] = processedMessages;

        if (context.Functions != null && context.Functions.Any())
        {
            requestObj["tools"] = PrepareFunctionDefinitions(context.Functions);

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
                                    // Handle files by converting to text reference
                                    contentArray.Add(new
                                    {
                                        type = "text",
                                        text = $"[Attached file: {filePart.FileName}]"
                                    });
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

    bool IsParameterSupported(string paramName, Domain.Enums.ModelType modelType, bool isTopLevelParam = false)
    {
        var supportedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temperature", "top_p", "frequency_penalty", "presence_penalty",
            "max_tokens", "stream", "stop", "logit_bias"
        };

        return supportedParams.Contains(paramName) || base.IsParameterSupported(paramName, modelType);
    }
}