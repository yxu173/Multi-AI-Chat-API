using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users; 

namespace Application.Services.PayloadBuilders;

public class GrokPayloadBuilder : BasePayloadBuilder, IGrokPayloadBuilder
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

    public AiRequestPayload PreparePayload(AiRequestContext context)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        var parameter = GetMergedParameters(context);
        if (parameter.TryGetValue("temperature", out var temperature))
        {
            requestObj["temperature"] = 0;
        }

    //    requestObj["reasoning_effort"] = "high";

        var processedMessages = ProcessMessagesForGrokInput(context.History, context.AiAgent, context.UserSettings);
        requestObj["messages"] = processedMessages;

        return new AiRequestPayload(requestObj);
    }

    private List<object> ProcessMessagesForGrokInput(
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
            Logger?.LogDebug("Adding system instructions as a message for Grok.");
        }

        foreach (var message in history)
        {
            var role = message.IsFromAi ? "system" : "user";
            var rawContent = message.Content?.Trim() ?? "";

            if (string.IsNullOrEmpty(rawContent)) continue;

            if (!message.IsFromAi)
            {
                var contentParts = _multimodalContentParser.Parse(rawContent);
                var contentArray = new List<object>();
                
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            var txt = textPart.Text?.Trim();
                            if (!string.IsNullOrEmpty(txt))
                                contentArray.Add(new { 
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
                                    url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}",
                                    detail = "high"
                                }
                            });
                            break;
                    }
                }
                
                if (contentArray.Count > 0)
                {
                    processedMessages.Add(new { 
                        role = role, 
                        content = contentArray 
                    });
                }
            }
            else
            {
                processedMessages.Add(new { role = "assistant", content = rawContent });
            }
        }

        return processedMessages;
    }
}