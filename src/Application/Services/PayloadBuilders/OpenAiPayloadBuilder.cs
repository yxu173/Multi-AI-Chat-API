using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

// Added for Dictionary

// Added for LINQ methods like Any

namespace Application.Services.PayloadBuilders;

public class OpenAiPayloadBuilder : BasePayloadBuilder, IOpenAiPayloadBuilder
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

    public AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        var parameter = GetMergedParameters(context);
        ApplyParametersToRequest(requestObj, parameter, model.ModelType);

        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ??
                                context.UserSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            requestObj["instructions"] = systemMessage.Trim();
            Logger?.LogDebug("Adding system instructions for model {ModelCode}", model.ModelCode);
        }

        var processedMessages = ProcessMessagesForOpenAIInput(context.History);
        requestObj["input"] = processedMessages;

        // Handle Tools
        if (toolDefinitions?.Any() == true && IsParameterSupported("tools", model.ModelType))
        {
            var formattedTools = new List<object>();
            foreach (var toolDefObj in toolDefinitions)
            {
                IDictionary<string, object>? toolDict = toolDefObj as IDictionary<string, object>;

                if (toolDict == null)
                {
                    if (toolDefObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        try
                        {
                            toolDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
                        }
                        catch (Exception ex)
                        {
                            Logger?.LogWarning(ex,
                                "Failed to deserialize JsonElement tool definition: {ToolDefinition}",
                                jsonElement.GetRawText());
                        }
                    }
                }

                if (toolDict != null)
                {
                    if (toolDict.ContainsKey("type") && toolDict["type"]?.ToString() == "function")
                    {
                        if (toolDict.ContainsKey("function"))
                        {
                            formattedTools.Add(toolDict);
                            continue;
                        }

                        var functionObj = new Dictionary<string, object>();
                        foreach (var kvp in toolDict)
                        {
                            if (kvp.Key == "type") continue;
                            functionObj[kvp.Key] = kvp.Value;
                        }

                        var formattedTool = new Dictionary<string, object>
                        {
                            { "type", "function" },
                            { "function", functionObj }
                        };
                        formattedTools.Add(formattedTool);
                        continue;
                    }

                    if (toolDict.TryGetValue("name", out var name) && name is string)
                    {
                        var functionObj = new Dictionary<string, object>
                        {
                            { "name", name }
                        };
                        if (toolDict.TryGetValue("description", out var description))
                        {
                            functionObj.Add("description", description);
                        }

                        if (toolDict.TryGetValue("parameters", out var parameters))
                        {
                            functionObj.Add("parameters", parameters);
                        }

                        if (toolDict.TryGetValue("strict", out var strict))
                        {
                            functionObj.Add("strict", strict);
                        }

                        var formattedTool = new Dictionary<string, object>
                        {
                            { "type", "function" },
                            { "function", functionObj }
                        };
                        formattedTools.Add(formattedTool);
                    }
                    else
                    {
                        Logger?.LogWarning(
                            "Skipping tool definition missing required 'name' property: {ToolDefinition}",
                            System.Text.Json.JsonSerializer.Serialize(toolDefObj));
                    }
                }
                else
                {
                    Logger?.LogWarning("Skipping tool definition with unexpected format: {ToolDefinition}",
                        System.Text.Json.JsonSerializer.Serialize(toolDefObj));
                }
            }

            if (formattedTools.Any())
            {
                Logger?.LogInformation(
                    "Adding {ToolCount} formatted tool definitions to OpenAI payload for model {ModelCode}",
                    formattedTools.Count, model.ModelCode);
                requestObj["tools"] = formattedTools;

                if (IsParameterSupported("tool_choice", model.ModelType))
                {
                    requestObj["tool_choice"] = "auto";
                }
            }
        }

        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
        requestObj.Remove("stop");

        AddOpenAiSpecificParameters(requestObj, context, parameter);

        return new AiRequestPayload(requestObj);
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
                            Logger?.LogInformation("Adding file {FileName} using 'input_file' type.",
                                filePart.FileName);
                            openAiContentItems.Add(new
                            {
                                type = "input_file",
                                filename = filePart.FileName,
                                file_data = $"data:{filePart.MimeType};base64,{filePart.Base64Data}"
                            });
                            hasNonTextContent = true;
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

    private void AddOpenAiSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context,
        Dictionary<string, object> appliedParameters)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;

        if (useEffectiveThinking && IsParameterSupported("reasoning", context.SpecificModel.ModelType))
        {
            requestObj["reasoning"] = new { effort = "medium", summary = "detailed" };
            Logger?.LogDebug("Adding reasoning effort for OpenAI model {ModelCode}", context.SpecificModel.ModelCode);

            if (appliedParameters.ContainsKey("temperature")) requestObj.Remove("temperature");
            if (appliedParameters.ContainsKey("top_p")) requestObj.Remove("top_p");
            if (appliedParameters.ContainsKey("max_output_tokens")) requestObj.Remove("max_output_tokens");
            if (appliedParameters.ContainsKey("max_tokens")) requestObj.Remove("max_tokens");

            Logger?.LogDebug(
                "Removed potentially conflicting parameters (temp, top_p, max_tokens) due to reasoning effort.");
        }

        if (appliedParameters.TryGetValue("max_tokens", out var maxTokensValue) && !requestObj.ContainsKey("reasoning"))
        {
            if (requestObj.ContainsKey("max_tokens")) requestObj.Remove("max_tokens");
            if (!requestObj.ContainsKey("max_output_tokens"))
            {
                requestObj["max_output_tokens"] = maxTokensValue;
                Logger?.LogDebug("Mapped 'max_tokens' to 'max_output_tokens'.");
            }
        }
    }

    protected void ApplyParametersToRequest(Dictionary<string, object> requestObj,
        Dictionary<string, object> parameters, ModelType modelType)
    {
        foreach (var kvp in parameters)
        {
            string key = kvp.Key;
            object value = kvp.Value;

            if (key == "max_tokens")
            {
                key = "max_output_tokens";
            }

            if (IsParameterSupported(key, modelType))
            {
                requestObj[key] = value;
                Logger?.LogTrace("Applied parameter {ParameterKey}={ParameterValue}", key, value);
            }
            else
            {
                Logger?.LogWarning("Parameter {ParameterKey} is not supported for {ModelType} and was skipped.", key,
                    modelType);
            }
        }
    }
}