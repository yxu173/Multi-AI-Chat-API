using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Services.PayloadBuilders;

public class GrokPayloadBuilder : BasePayloadBuilder, IGrokPayloadBuilder
{
    private readonly MultimodalContentParser _multimodalContentParser;

    public GrokPayloadBuilder(
        MultimodalContentParser multimodalContentParser,
        ILogger<GrokPayloadBuilder> logger)
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

        var (systemPrompt, processedMessages) = ProcessMessagesForGrok(context.History, context);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            processedMessages.Insert(0, new { role = "system", content = systemPrompt });
        }
        requestObj["messages"] = processedMessages;

        if (toolDefinitions?.Any() == true)
        {
             Logger?.LogWarning("Grok API does not have a standard documented format for tool definitions in the chat completions endpoint. Ignoring {ToolCount} tools.", toolDefinitions.Count);
        }

        AddGrokSpecificParameters(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private (string? SystemPrompt, List<object> Messages) ProcessMessagesForGrok(List<MessageDto> history, AiRequestContext context)
    {
        string? agentSystemMessage = context.AiAgent?.ModelParameter.SystemInstructions;
        string? userSystemMessage = context.UserSettings?.ModelParameters.SystemInstructions;
        string? finalSystemPrompt = agentSystemMessage ?? userSystemMessage;

        var otherMessages = new List<object>();
        var mergedHistory = MergeConsecutiveRoles(
            history.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = _multimodalContentParser.Parse(rawContent);
            var grokRole = role; 

            if (contentParts.Count > 1 || contentParts.Any(p => p is ImagePart))
            {
                var grokContentItems = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart tp:
                            grokContentItems.Add(new { type = "text", text = tp.Text });
                            break;
                        case ImagePart ip:
                            if (IsValidGrokImageType(ip.MimeType))
                            {
                                grokContentItems.Add(new
                                {
                                    type = "image_url",
                                    image_url = new { url = $"data:{ip.MimeType};base64,{ip.Base64Data}", detail = "high" } // 'detail' could be configurable
                                });
                            }
                            else
                            {
                                Logger?.LogWarning("Unsupported image type '{MimeType}' for Grok. Sending placeholder text.", ip.MimeType);
                                grokContentItems.Add(new { type = "text", text = $"[Image: {ip.FileName ?? ip.MimeType} - Unsupported Type]" });
                            }
                            break;
                        case FilePart fp: 
                            Logger?.LogWarning("Document type '{MimeType}' is not directly supported by Grok multimodal input. Sending placeholder text for file {FileName}.", fp.MimeType, fp.FileName);
                            grokContentItems.Add(new { type = "text", text = $"[Document Attached: {fp.FileName} - Type ({fp.MimeType}) Not Directly Supported]" });
                            break;
                    }
                }
                if (grokContentItems.Any())
                {
                    otherMessages.Add(new { role = grokRole, content = grokContentItems.ToArray() });
                }
            }
            else if (contentParts.Count == 1 && contentParts[0] is TextPart singleTextPart)
            {
                otherMessages.Add(new { role = grokRole, content = singleTextPart.Text });
            }
        }

        EnsureAlternatingRoles(otherMessages, "user", "assistant");
        return (finalSystemPrompt?.Trim(), otherMessages);
    }

    private bool IsValidGrokImageType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return false;
        var normalizedMime = mimeType.ToLowerInvariant();
        return normalizedMime == "image/jpeg" || normalizedMime == "image/png" || normalizedMime == "image/gif" || normalizedMime == "image/webp";
    }

    private void AddGrokSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        if (useEffectiveThinking && !requestObj.ContainsKey("reasoning_effort"))
        {
            if (IsParameterSupported("reasoning_effort", ModelType.Grok))
            {
                 requestObj["reasoning_effort"] = "high"; 
                 Logger?.LogDebug("Enabled Grok 'reasoning_effort=high' parameter based on effective thinking setting ({UseThinking})", useEffectiveThinking);
            }
            else
            {
                Logger?.LogWarning("Requested thinking (reasoning_effort) for Grok model {ModelCode}, but the parameter is not marked as supported for this model type/variant.", context.SpecificModel.ModelCode);
            }
        }
    }
}