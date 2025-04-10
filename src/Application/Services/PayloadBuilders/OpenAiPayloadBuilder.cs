using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.PayloadBuilders;

public class OpenAiPayloadBuilder : BasePayloadBuilder, IOpenAiPayloadBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public OpenAiPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<OpenAiPayloadBuilder> logger)
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

        var processedMessages = ProcessMessagesForOpenAI(context.History, context);
        requestObj["messages"] = processedMessages;

        if (toolDefinitions?.Any() == true && IsParameterSupported("tools", model.ModelType))
        {
            Logger?.LogInformation("Adding {ToolCount} tool definitions to OpenAI payload for model {ModelCode}",
                toolDefinitions.Count, model.ModelCode);
            requestObj["tools"] = toolDefinitions;
            if (IsParameterSupported("tool_choice", model.ModelType))
            {
                requestObj["tool_choice"] = "auto";
            }
        }

        AddOpenAiSpecificParameters(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private List<object> ProcessMessagesForOpenAI(List<MessageDto> history, AiRequestContext context)
    {
        var processedMessages = new List<object>();
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ?? context.UserSettings?.ModelParameters.SystemInstructions;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
        }

        if (useEffectiveThinking)
        {
            processedMessages.Add(new
            {
                role = "system",
                content =
                    "When solving complex problems, show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly."
            });
        }

        var mergedHistory = MergeConsecutiveRoles(history.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
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
                            openAiContentItems.Add(new { type = "text", text = textPart.Text });
                            break;
                        case ImagePart imagePart:
                            openAiContentItems.Add(new
                            {
                                type = "image_url",
                                image_url = new { url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}" }
                            });
                            hasNonTextContent = true; 
                            break;
                        case FilePart filePart:
                            // Use the user-specified format for files
                            Logger?.LogInformation("Adding file {FileName} using custom 'file' type structure.", filePart.FileName);
                            openAiContentItems.Add(new
                            {
                                type = "file",
                                file = new 
                                {
                                    filename = filePart.FileName, 
                                    file_data = $"data:{filePart.MimeType};base64,{filePart.Base64Data}" 
                                }
                            });
                            hasNonTextContent = true; 
                            break;
                    }
                }

                if (openAiContentItems.Any())
                {
                    if (hasNonTextContent)
                    {
                        processedMessages.Add(new { role = "user", content = openAiContentItems.ToArray() });
                    }
                    else
                    {
                        string combinedText = string.Join("\n",
                            openAiContentItems.Select(item => item.GetType().GetProperty("text")?.GetValue(item)?.ToString() ?? "")).Trim();
                        if (!string.IsNullOrEmpty(combinedText))
                        {
                            processedMessages.Add(new { role = "user", content = combinedText });
                        }
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

    private void AddOpenAiSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        if (useEffectiveThinking)
        {
            Logger?.LogDebug("OpenAI reasoning is handled via system prompt for model {ModelCode}", context.SpecificModel.ModelCode);
        }

        if (context.SpecificModel.SupportsVision)
        {
            // Placeholder for potential future vision-specific params
        }
    }
} 