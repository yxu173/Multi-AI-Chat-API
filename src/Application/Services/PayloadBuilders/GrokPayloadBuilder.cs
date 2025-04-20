using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users; // Required for potential JSON operations if needed later

namespace Application.Services.PayloadBuilders;

public class GrokPayloadBuilder : BasePayloadBuilder, IGrokPayloadBuilder
{
    // Inject necessary services if needed (e.g., for multimodal parsing, though Grok might not support it yet)
    // private readonly MultimodalContentParser _multimodalContentParser;

    public GrokPayloadBuilder(
        // MultimodalContentParser multimodalContentParser, // Uncomment if needed
        ILogger<GrokPayloadBuilder> logger)
        : base(logger)
    {
        // _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
    }

    public AiRequestPayload PreparePayload(AiRequestContext context)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        // Only include the essential parameters as in the curl example
        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        // Only add temperature if it exists in the parameters
        var parameter = GetMergedParameters(context);
        if (parameter.TryGetValue("temperature", out var temperature))
        {
            requestObj["temperature"] = 0;
        }

        // Process messages for Grok's format
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

        // 1. Add System Prompt (if any) as the first message
        string? systemMessage = aiAgent?.ModelParameter.SystemInstructions ??
                                userSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
            Logger?.LogDebug("Adding system instructions as a message for Grok.");
        }

        // 2. Add conversation history
        foreach (var message in history)
        {
            var role = message.IsFromAi ? "system" : "user";
            var rawContent = message.Content?.Trim() ?? "";

            if (string.IsNullOrEmpty(rawContent)) continue;

            // Simple message format matching the curl example
            processedMessages.Add(new { role, content = rawContent });
        }

        return processedMessages;
    }
} 