using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.PayloadBuilders;

public class DeepSeekPayloadBuilder : BasePayloadBuilder, IDeepSeekPayloadBuilder
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
        List<object>? toolDefinitions,
        CancellationToken cancellationToken)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        var parameters = GetMergedParameters(context);
        ApplyParametersToRequest(requestObj, parameters, model.ModelType);

        var processedMessages = await ProcessMessagesForDeepSeekAsync(context, cancellationToken);
        requestObj["messages"] = processedMessages;

        if (toolDefinitions?.Any() == true)
        {
            Logger?.LogWarning(
                "Tool definitions were provided for DeepSeek model {ModelCode}, but tool calling is not currently implemented/supported in this builder.",
                model.ModelCode);
            if (IsParameterSupported("tools", model.ModelType))
            {
                requestObj["tools"] = toolDefinitions;
                if (IsParameterSupported("tool_choice", model.ModelType))
                {
                    requestObj["tool_choice"] = "auto";
                }
            }
        }

        AddDeepSeekSpecificParameters(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private async Task<List<object>> ProcessMessagesForDeepSeekAsync(AiRequestContext context,
        CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        string? systemMessage = context.AiAgent?.SystemInstructions ?? context.UserSettings?.SystemMessage;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
        }

        // Add thinking prompt via system message if enabled (and not handled by parameters)
        if (useEffectiveThinking && !context.SpecificModel.ModelCode.ToLower().Contains("reasoner"))
        {
            var parameters = GetMergedParameters(context);
            bool cotEnabled = parameters.TryGetValue("enable_cot", out var cotVal) && cotVal is true;
            bool reasoningEnabled = parameters.TryGetValue("enable_reasoning", out var reaVal) && reaVal is true;

            if (!cotEnabled && !reasoningEnabled)
            {
                processedMessages.Add(new
                {
                    role = "system",
                    content =
                        "When solving complex problems, please show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly."
                });
                Logger?.LogDebug("Added thinking system prompt for DeepSeek model {ModelCode}",
                    context.SpecificModel.ModelCode);
            }
            else
            {
                Logger?.LogDebug("DeepSeek thinking likely handled by enable_cot/enable_reasoning parameters.");
            }
        }

        var mergedHistory = MergeConsecutiveRoles(
            context.History.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = _multimodalContentParser.Parse(rawContent);
            string contentText = string.Join("\n", contentParts.Select(p => p switch
            {
                TextPart tp => tp.Text,
                ImagePart ip => $"[Image: {ip.FileName ?? ip.MimeType} - Not Sent]",
                FilePart fp => $"[File: {fp.FileName} ({fp.MimeType}) - Not Sent]",
                _ => ""
            })).Trim();

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
                    new { role = "user", content = "Proceed." }); // Simple placeholder
                Logger?.LogWarning(
                    "Inserted placeholder user message for DeepSeek reasoner model {ModelCode} as the first non-system message was not 'user'.",
                    context.SpecificModel.ModelCode);
            }
        }


        await Task.CompletedTask;
        return processedMessages;
    }

    private void AddDeepSeekSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        if (useEffectiveThinking)
        {
            if (IsParameterSupported("enable_cot", ModelType.DeepSeek) && !requestObj.ContainsKey("enable_cot"))
            {
                requestObj["enable_cot"] = true;
                Logger?.LogDebug(
                    "Enabled DeepSeek 'enable_cot' parameter (Effective: {UseThinking}) for model {ModelCode}",
                    useEffectiveThinking, context.SpecificModel.ModelCode);
            }

            if (IsParameterSupported("enable_reasoning", ModelType.DeepSeek) &&
                !requestObj.ContainsKey("enable_reasoning"))
            {
                requestObj["enable_reasoning"] = true;
                Logger?.LogDebug(
                    "Enabled DeepSeek 'enable_reasoning' parameter (Effective: {UseThinking}) for model {ModelCode}",
                    useEffectiveThinking, context.SpecificModel.ModelCode);
            }
        }
    }
}