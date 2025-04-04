using System.Text;
using System.Text.RegularExpressions;
using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes; // Needed for JsonObject in tool schemas
using Domain.Repositories; // Added for IChatSessionPluginRepository

namespace Application.Services;

public record AiRequestContext(
    Guid UserId,
    ChatSession ChatSession,
    List<MessageDto> History,
    AiAgent? AiAgent,
    UserAiModelSettings? UserSettings,
    AiModel SpecificModel,
    bool? RequestSpecificThinking = null
);

public interface IAiRequestHandler
{
    Task<AiRequestPayload> PrepareRequestPayloadAsync(
        AiRequestContext context,
        CancellationToken cancellationToken = default);
}

public abstract record ContentPart;

public record TextPart(string Text) : ContentPart;

public record ImagePart(string MimeType, string Base64Data, string? FileName = null) : ContentPart;

public record FilePart(string MimeType, string Base64Data, string FileName) : ContentPart;

public class AiRequestHandler : IAiRequestHandler
{
    private readonly ILogger<AiRequestHandler>? _logger;
    private readonly IAiModelServiceFactory _serviceFactory;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository; // Added dependency

    private static readonly Regex MultimodalTagRegex =
        new Regex(@"<(image|file)-base64:(?:([^:]*?):)?([^;]*?);base64,([^>]*?)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public AiRequestHandler(
        IAiModelServiceFactory serviceFactory,
        IPluginExecutorFactory pluginExecutorFactory,
        IChatSessionPluginRepository chatSessionPluginRepository, // Added parameter
        ILogger<AiRequestHandler>? logger = null)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _pluginExecutorFactory =
            pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _chatSessionPluginRepository = chatSessionPluginRepository ??
                                       throw new ArgumentNullException(
                                           nameof(chatSessionPluginRepository)); // Store dependency
        _logger = logger;
    }

    public async Task<AiRequestPayload> PrepareRequestPayloadAsync(AiRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ChatSession);
        ArgumentNullException.ThrowIfNull(context.SpecificModel);
        ArgumentNullException.ThrowIfNull(context.History);

        var modelType = context.SpecificModel.ModelType;
        var chatId = context.ChatSession.Id;

        bool modelMightSupportTools = modelType is ModelType.OpenAi or ModelType.Anthropic or ModelType.Gemini;
        List<object>? toolDefinitions = null;

        if (modelMightSupportTools)
        {
            // Fetch active plugins for THIS chat session
            var activePlugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatId, cancellationToken);
            var activePluginIds = activePlugins.Select(p => p.PluginId).ToList();

            if (activePluginIds.Any())
            {
                _logger?.LogInformation("Found {Count} active plugins for ChatSession {ChatId}", activePluginIds.Count,
                    chatId);
                // Pass the active IDs to the helper
                toolDefinitions = GetToolDefinitionsForPayload(modelType, activePluginIds);
            }
            else
            {
                _logger?.LogInformation("No active plugins found for ChatSession {ChatId}", chatId);
            }
        }

        try
        {
            var payload = modelType switch
            {
                ModelType.OpenAi => await PrepareOpenAiPayloadAsync(context, toolDefinitions, cancellationToken),
                ModelType.Anthropic => await PrepareAnthropicPayloadAsync(context, toolDefinitions, cancellationToken),
                ModelType.Gemini => await PrepareGeminiPayloadAsync(context, toolDefinitions, cancellationToken),
                ModelType.DeepSeek => await PrepareDeepSeekPayloadAsync(context, cancellationToken),
                _ => throw new NotSupportedException(
                    $"Model type {modelType} is not supported for request preparation."),
            };
            return payload;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error preparing payload for {ModelType}", modelType);
            throw;
        }
    }

    private async Task<AiRequestPayload> PrepareOpenAiPayloadAsync(AiRequestContext context,
        List<object>? toolDefinitions, CancellationToken cancellationToken)
    {
        var payload = PrepareOpenAiPayload(context, toolDefinitions);
        await Task.CompletedTask;
        return payload;
    }

    private AiRequestPayload PrepareOpenAiPayload(AiRequestContext context, List<object>? toolDefinitions)
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
            _logger?.LogInformation("Adding {ToolCount} tool definitions to OpenAI payload for model {ModelCode}",
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

   private async Task<AiRequestPayload> PrepareAnthropicPayloadAsync(
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
    var (systemPrompt, processedMessages) = ProcessMessagesForAnthropic(context.History, context);
    if (!string.IsNullOrWhiteSpace(systemPrompt))
    {
        requestObj["system"] = systemPrompt;
    }

    requestObj["messages"] = processedMessages;

    // Add tool definitions if available
    if (toolDefinitions?.Any() == true && IsParameterSupported("tools", model.ModelType))
    {
        requestObj["tools"] = toolDefinitions;
        if (IsParameterSupported("tool_choice", model.ModelType))
        {
            // Set tool_choice as an object instead of a string
            requestObj["tool_choice"] = new { type = "auto" };
        }
    }

    AddAnthropicSpecificParameters(requestObj, context);

    // Additional logic for thinking parameter and max_tokens (unchanged)
    bool useEffectiveThinking = context.RequestSpecificThinking ?? model.SupportsThinking;
    if (useEffectiveThinking && !requestObj.ContainsKey("thinking"))
    {
        const int defaultThinkingBudget = 1024;
        requestObj["temperature"] = 1;
        requestObj.Remove("top_k");
        requestObj.Remove("top_p");
        requestObj["thinking"] = new { type = "enabled", budget_tokens = defaultThinkingBudget };
        _logger?.LogDebug("Enabled Anthropic 'thinking' parameter with budget {Budget} (Effective: {UseThinking})",
            defaultThinkingBudget, useEffectiveThinking);
    }

    if (!requestObj.ContainsKey("max_tokens"))
    {
        const int defaultMaxTokens = 4096;
        requestObj["max_tokens"] = defaultMaxTokens;
        _logger?.LogWarning("Anthropic request payload was missing 'max_tokens'. Added default value: {DefaultMaxTokens}", defaultMaxTokens);
    }

    return new AiRequestPayload(requestObj);
}

    private async Task<AiRequestPayload> PrepareGeminiPayloadAsync(
        AiRequestContext context,
        List<object>? toolDefinitions,
        CancellationToken cancellationToken)
    {
        var requestObj = new Dictionary<string, object>();
        var generationConfig = new Dictionary<string, object>();
        var safetySettings = GetGeminiSafetySettings();

        var parameters = GetMergedParameters(context);
        ApplyGeminiParametersToConfig(generationConfig, parameters, context.SpecificModel.ModelType);

        var geminiContents = await ProcessMessagesForGeminiAsync(context.History, context, cancellationToken);

        requestObj["contents"] = geminiContents;
        requestObj["generationConfig"] = generationConfig;
        requestObj["safetySettings"] = safetySettings;

        // Add tool definitions if available
        if (toolDefinitions?.Any() == true && IsParameterSupported("tools", context.SpecificModel.ModelType))
        {
            requestObj["tools"] = new[] { new { functionDeclarations = toolDefinitions } };
        }

        return new AiRequestPayload(requestObj);
    }

    private async Task<AiRequestPayload> PrepareDeepSeekPayloadAsync(AiRequestContext context,
        CancellationToken cancellationToken)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        var parameters = GetMergedParameters(context);
        ApplyParametersToRequest(requestObj, parameters, model.ModelType);

        var processedMessages = await ProcessMessagesForDeepSeekAsync(context.History, context, cancellationToken);
        requestObj["messages"] = processedMessages;

        AddDeepSeekSpecificParameters(requestObj, context);

        await Task.CompletedTask;
        return new AiRequestPayload(requestObj);
    }

    private Dictionary<string, object> GetMergedParameters(AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        var model = context.SpecificModel;
        var agent = context.AiAgent;
        var userSettings = context.UserSettings;

        ModelParameters? sourceParams = null;
        if (agent?.AssignCustomModelParameters == true && agent.ModelParameter != null)
        {
            sourceParams = agent.ModelParameter;
            if (sourceParams.Temperature.HasValue) parameters["temperature"] = sourceParams.Temperature.Value;
            if (sourceParams.TopP.HasValue) parameters["top_p"] = sourceParams.TopP.Value;
            if (sourceParams.TopK.HasValue) parameters["top_k"] = sourceParams.TopK.Value;
            if (sourceParams.FrequencyPenalty.HasValue)
                parameters["frequency_penalty"] = sourceParams.FrequencyPenalty.Value;
            if (sourceParams.PresencePenalty.HasValue)
                parameters["presence_penalty"] = sourceParams.PresencePenalty.Value;
            if (sourceParams.MaxTokens.HasValue) parameters["max_tokens"] = sourceParams.MaxTokens.Value;
            if (sourceParams.StopSequences?.Any() == true) parameters["stop"] = sourceParams.StopSequences;
        }
        else if (userSettings != null)
        {
            if (userSettings.Temperature.HasValue) parameters["temperature"] = userSettings.Temperature.Value;
            if (userSettings.TopP.HasValue) parameters["top_p"] = userSettings.TopP.Value;
            if (userSettings.TopK.HasValue) parameters["top_k"] = userSettings.TopK.Value;
            if (userSettings.FrequencyPenalty.HasValue)
                parameters["frequency_penalty"] = userSettings.FrequencyPenalty.Value;
            if (userSettings.PresencePenalty.HasValue)
                parameters["presence_penalty"] = userSettings.PresencePenalty.Value;
            if (userSettings.StopSequences?.Any() == true) parameters["stop"] = userSettings.StopSequences;
        }

        if (!parameters.ContainsKey("max_tokens") && model.MaxOutputTokens.HasValue)
        {
            parameters["max_tokens"] = model.MaxOutputTokens.Value;
        }

        return parameters;
    }

    private void ApplyParametersToRequest(Dictionary<string, object> requestObj, Dictionary<string, object> parameters,
        ModelType modelType)
    {
        foreach (var kvp in parameters)
        {
            string standardParamName = kvp.Key;
            string providerParamName = GetProviderParameterName(standardParamName, modelType);

            if (IsParameterSupported(providerParamName, modelType))
            {
                object valueToSend = kvp.Value;
                requestObj[providerParamName] = valueToSend;
            }
            else
            {
                _logger?.LogDebug(
                    "Skipping unsupported parameter '{StandardName}' (mapped to '{ProviderName}') for model type {ModelType}",
                    standardParamName, providerParamName, modelType);
            }
        }
    }

    private List<object> ProcessMessagesForOpenAI(List<MessageDto> history, AiRequestContext context)
    {
        var processedMessages = new List<object>();
        bool thinkingEnabled = context.SpecificModel.SupportsThinking;
        string? systemMessage = context.AiAgent?.SystemInstructions ?? context.UserSettings?.SystemMessage;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
        }

        if (thinkingEnabled)
        {
            processedMessages.Add(new
            {
                role = "system",
                content =
                    "When solving complex problems, show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly."
            });
        }

        foreach (var msg in history)
        {
            string role = msg.IsFromAi ? "assistant" : "user";
            string rawContent = msg.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(rawContent)) continue;

            if (role == "user")
            {
                var contentParts = ParseMultimodalContent(rawContent);
                var openAiContentItems = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            openAiContentItems.Add(new { type = "text", text = textPart.Text }); break;
                        case ImagePart imagePart:
                            openAiContentItems.Add(new
                            {
                                type = "image_url",
                                image_url = new { url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}" }
                            }); break;
                        case FilePart filePart: // Add as text placeholder
                            openAiContentItems.Add(
                                new { type = "text", text = $"[Attached File: {filePart.FileName}]" });
                            break;
                    }
                }

                // Determine final content format (string or array)
                if (openAiContentItems.Any())
                {
                    // Check if it's purely text after parsing
                    if (openAiContentItems.All(item =>
                            item.GetType().GetProperty("type")?.GetValue(item)?.ToString() == "text"))
                    {
                        string combinedText = string.Join("\n",
                            openAiContentItems.Select(item =>
                                item.GetType().GetProperty("text")?.GetValue(item)?.ToString() ?? "")).Trim();
                        // Only add if combined text is not empty
                        if (!string.IsNullOrEmpty(combinedText))
                        {
                            processedMessages.Add(new { role = "user", content = combinedText });
                        }
                    }
                    else
                    {
                        // Contains images or was originally multi-part, send as array
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

    private (string? SystemPrompt, List<object> Messages) ProcessMessagesForAnthropic(List<MessageDto> history,
        AiRequestContext context)
    {
        bool thinkingEnabled = context.SpecificModel.SupportsThinking;
        string? agentSystemMessage = context.AiAgent?.SystemInstructions;
        string? finalSystemPrompt = agentSystemMessage?.Trim() ?? context.UserSettings?.SystemMessage?.Trim();

        var otherMessages = new List<object>();
        var mergedHistory =
            MergeConsecutiveRoles(history.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? ""))
                .ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            string anthropicRole = (role == "assistant") ? "assistant" : "user";
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = ParseMultimodalContent(rawContent);
            if (contentParts.Count > 1 || contentParts.Any(p => p is not TextPart))
            {
                // Multimodal
                var anthropicContentItems = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart tp: anthropicContentItems.Add(new { type = "text", text = tp.Text }); break;
                        case ImagePart ip:
                            if (IsValidAnthropicImageType(ip.MimeType, out var mediaType))
                            {
                                anthropicContentItems.Add(new
                                {
                                    type = "image",
                                    source = new { type = "base64", media_type = mediaType, data = ip.Base64Data }
                                });
                            }
                            else
                            {
                                anthropicContentItems.Add(new
                                {
                                    type = "text", text = $"[Image: {ip.FileName ?? ip.MimeType} - Unsupported Type]"
                                });
                            }

                            break;
                        case FilePart fp:
                            if (IsValidAnthropicDocumentType(fp.MimeType, out var docMediaType))
                            {
                                anthropicContentItems.Add(new
                                {
                                    type = "document",
                                    source = new
                                    {
                                        type = "base64",
                                        media_type = docMediaType,
                                        data = fp.Base64Data
                                    }
                                });
                            }
                            else
                            {
                                anthropicContentItems.Add(new
                                {
                                    type = "text",
                                    text = $"[Document: {fp.FileName} - Unsupported Type ({fp.MimeType})]"
                                });
                            }

                            break;
                    }
                }

                if (anthropicContentItems.Any())
                    otherMessages.Add(new { role = anthropicRole, content = anthropicContentItems.ToArray() });
            }
            else if (contentParts.Count == 1 && contentParts[0] is TextPart singleTextPart)
            {
                // Single text part message
                otherMessages.Add(new { role = anthropicRole, content = singleTextPart.Text });
            }
            // Ignore if contentParts is empty (shouldn't happen with current ParseMultimodalContent logic)
        }

        EnsureAlternatingRoles(otherMessages, "user", "assistant");
        return (finalSystemPrompt, otherMessages);
    }

    private async Task<List<object>> ProcessMessagesForGeminiAsync(
        List<MessageDto> history,
        AiRequestContext context,
        CancellationToken cancellationToken)
    {
        bool thinkingEnabled = context.SpecificModel.SupportsThinking;
        string? systemMessage = context.AiAgent?.SystemInstructions ?? context.UserSettings?.SystemMessage;
        var geminiContents = new List<object>();

        var systemPrompts = new List<string>();
        if (!string.IsNullOrWhiteSpace(systemMessage)) systemPrompts.Add(systemMessage.Trim());
        if (thinkingEnabled)
            systemPrompts.Add(
                "When solving complex problems, show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly.");
        string combinedSystem = string.Join("\n\n", systemPrompts);

        var historyToProcess = new List<(string Role, string Content)>();
        bool systemInjected = false;
        foreach (var msg in history)
        {
            string role = msg.IsFromAi ? "model" : "user";
            string content = msg.Content?.Trim() ?? "";

            if (!systemInjected && role == "user" && !string.IsNullOrWhiteSpace(combinedSystem))
            {
                content = $"{combinedSystem}\n\n{content}";
                systemInjected = true;
            }

            historyToProcess.Add((role, content));
        }

        if (!systemInjected && !string.IsNullOrWhiteSpace(combinedSystem) &&
            !historyToProcess.Any(h => h.Role == "user"))
        {
            historyToProcess.Insert(0, ("user", combinedSystem));
            _logger?.LogWarning("Prepended Gemini system/thinking prompt as initial user message.");
        }

        var mergedHistory = MergeConsecutiveRoles(historyToProcess);

        IAiFileUploader? fileUploader = null;
        bool needsFileUpload = mergedHistory.Any(m => ParseMultimodalContent(m.Content).OfType<FilePart>().Any());

        if (needsFileUpload)
        {
            var modelService = _serviceFactory.GetService(context.UserId, context.SpecificModel.Id,
                context.ChatSession.CustomApiKey);
            if (modelService is IAiFileUploader uploader)
            {
                fileUploader = uploader;
                _logger?.LogInformation("File uploader service obtained for provider {ProviderType}",
                    modelService.GetType().Name);
            }
            else
            {
                _logger?.LogWarning(
                    "The AI service {ServiceType} for model {ModelCode} does not implement IAiFileUploader. Files will be added as placeholders.",
                    modelService.GetType().Name, context.SpecificModel.ModelCode);
            }
        }

        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = ParseMultimodalContent(rawContent);
            var geminiParts = new List<object>();

            foreach (var part in contentParts)
            {
                // Treat both ImagePart and FilePart by uploading via File API
                if (part is TextPart tp)
                {
                    geminiParts.Add(new { text = tp.Text });
                }
                else if (part is ImagePart ip || part is FilePart fp)
                {
                    string fileName = (part is FilePart fileP)
                        ? fileP.FileName
                        : ((ImagePart)part).FileName ?? "image.tmp";
                    string mimeType = (part is FilePart fileP2) ? fileP2.MimeType : ((ImagePart)part).MimeType;
                    string base64Data = (part is FilePart fileP3) ? fileP3.Base64Data : ((ImagePart)part).Base64Data;
                    string partTypeName = part.GetType().Name.Replace("Part", "");

                    if (fileUploader != null)
                    {
                        try
                        {
                            byte[] fileBytes = Convert.FromBase64String(base64Data);
                            _logger?.LogInformation(
                                "Uploading {PartType} {FileName} ({MimeType}) to Gemini File API...",
                                partTypeName, fileName, mimeType);
                            var uploadResult =
                                await fileUploader.UploadFileForAiAsync(fileBytes, mimeType, fileName,
                                    cancellationToken);

                            if (uploadResult != null)
                            {
                                geminiParts.Add(new
                                {
                                    fileData = new { mimeType = uploadResult.MimeType, fileUri = uploadResult.Uri }
                                });
                                _logger?.LogInformation("{PartType} {FileName} uploaded successfully. URI: {FileUri}",
                                    partTypeName, fileName, uploadResult.Uri);
                            }
                            else
                            {
                                geminiParts.Add(new { text = $"[{partTypeName} Upload Failed: {fileName}]" });
                            }
                        }
                        catch (FormatException ex)
                        {
                            _logger?.LogError(ex, "Invalid Base64 data for {PartType} {FileName}", partTypeName,
                                fileName);
                            geminiParts.Add(new { text = $"[Invalid {partTypeName} Data: {fileName}]" });
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error during Gemini file upload processing for {FileName}",
                                fileName);
                            geminiParts.Add(new { text = $"[{partTypeName} Processing Error: {fileName}]" });
                        }
                    }
                    else
                    {
                        // GeminiService not available or doesn't support upload, add placeholder
                        geminiParts.Add(new { text = $"[Attached {partTypeName}: {fileName} - Uploader N/A]" });
                    }
                }
            }

            if (geminiParts.Any())
            {
                geminiContents.Add(new { role = role, parts = geminiParts.ToArray() });
            }
        }

        EnsureAlternatingRoles(geminiContents, "user", "model");
        return geminiContents;
    }

    private async Task<List<object>> ProcessMessagesForDeepSeekAsync(List<MessageDto> history, AiRequestContext context,
        CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();
        bool thinkingEnabled = context.SpecificModel.SupportsThinking;
        string? systemMessage = context.AiAgent?.SystemInstructions ?? context.UserSettings?.SystemMessage;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
        }

        if (thinkingEnabled)
        {
            processedMessages.Add(new
            {
                role = "system",
                content =
                    "When solving complex problems, please show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly."
            });
        }

        var mergedHistory =
            MergeConsecutiveRoles(history.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? ""))
                .ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;
            var contentParts = ParseMultimodalContent(rawContent);
            string contentText;
            if (contentParts.Count > 1 || contentParts.Any(p => p is not TextPart))
            {
                contentText = string.Join("\n", contentParts.Select(p => p switch
                {
                    TextPart tp => tp.Text,
                    ImagePart ip => $"[Image: {ip.FileName ?? ip.MimeType}]",
                    FilePart fp => $"[File: {fp.FileName} ({fp.MimeType})]",
                    _ => ""
                })).Trim();
            }
            else
            {
                contentText = rawContent;
            }

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
                processedMessages.Insert(firstNonSystemIndex == -1 ? 0 : firstNonSystemIndex,
                    new { role = "user", content = "Proceed." });
                _logger?.LogWarning("Inserted placeholder user message for DeepSeek reasoner model.");
            }
        }

        await Task.CompletedTask;
        return processedMessages;
    }

    private void AddOpenAiSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        if (useEffectiveThinking)
        {
            bool isReasoningSupportedForModel = false;

            if (isReasoningSupportedForModel &&
                IsParameterSupported("reasoning_effort", context.SpecificModel.ModelType))
            {
                if (!requestObj.ContainsKey("reasoning_effort"))
                {
                    requestObj["reasoning_effort"] = "medium";
                    _logger?.LogDebug("Adding reasoning_effort: medium for OpenAI model {ModelCode}",
                        context.SpecificModel.ModelCode);
                }
            }
            else if (useEffectiveThinking)
            {
                _logger?.LogDebug("OpenAI reasoning enabled via system prompt instead of reasoning_effort parameter");
            }
        }
    }

    private void AddAnthropicSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        // Anthropic automatically removes unsupported params based on IsParameterSupported
    }

    private void ApplyGeminiParametersToConfig(Dictionary<string, object> generationConfig,
        Dictionary<string, object> parameters, ModelType modelType)
    {
        var supported = new HashSet<string>
            { "temperature", "topP", "topK", "maxOutputTokens", "stopSequences", "candidateCount" };
        foreach (var kvp in parameters)
        {
            string geminiName = GetProviderParameterName(kvp.Key, modelType);
            if (supported.Contains(geminiName))
            {
                generationConfig[geminiName] = kvp.Value;
            }
        }
    }

    private List<object> GetGeminiSafetySettings()
    {
        return new List<object>
        {
            new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
        };
    }

    private void AddDeepSeekSpecificParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useEffectiveThinking = context.RequestSpecificThinking ?? context.SpecificModel.SupportsThinking;
        if (useEffectiveThinking)
        {
            if (!requestObj.ContainsKey("enable_cot"))
            {
                requestObj["enable_cot"] = true;
                _logger?.LogDebug("Enabled DeepSeek 'enable_cot' parameter (Effective: {UseThinking})",
                    useEffectiveThinking);
            }

            if (!requestObj.ContainsKey("enable_reasoning"))
            {
                requestObj["enable_reasoning"] = true;
                _logger?.LogDebug("Enabled DeepSeek 'enable_reasoning' parameter (Effective: {UseThinking})",
                    useEffectiveThinking);
            }
        }
    }

    private List<ContentPart> ParseMultimodalContent(string messageContent)
    {
        var contentParts = new List<ContentPart>();
        if (string.IsNullOrEmpty(messageContent)) return contentParts;
        var lastIndex = 0;

        try
        {
            foreach (Match match in MultimodalTagRegex.Matches(messageContent))
            {
                if (match.Index > lastIndex)
                {
                    string textBefore = messageContent.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrEmpty(textBefore)) contentParts.Add(new TextPart(textBefore));
                }

                string tagType = match.Groups[1].Value.ToLowerInvariant();
                string? potentialFileName = match.Groups[2].Value;
                string? potentialMimeType = match.Groups[3].Value;
                string base64Data = match.Groups[4].Value;

                // Basic validation
                if (string.IsNullOrWhiteSpace(base64Data))
                {
                    _logger?.LogWarning("Malformed tag (missing base64 data): {Tag}", match.Value);
                    contentParts.Add(new TextPart(match.Value));
                    lastIndex = match.Index + match.Length;
                    continue;
                }

                // Extract and validate MimeType
                string? mimeType = potentialMimeType?.Trim();
                if (string.IsNullOrEmpty(mimeType) || !mimeType.Contains('/'))
                {
                    _logger?.LogWarning("Malformed tag (invalid or missing mime type '{MimeType}'): {Tag}", mimeType,
                        match.Value);
                    contentParts.Add(new TextPart(match.Value));
                    lastIndex = match.Index + match.Length;
                    continue;
                }

                if (tagType == "image")
                {
                    contentParts.Add(new ImagePart(mimeType, base64Data, potentialFileName));
                }
                else if (tagType == "file")
                {
                    string? fileName = potentialFileName?.Trim();
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        contentParts.Add(new FilePart(mimeType, base64Data, fileName));
                    }
                    else
                    {
                        _logger?.LogWarning("Malformed file tag (missing filename): {Tag}", match.Value);
                        contentParts.Add(new TextPart(match.Value));
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < messageContent.Length)
            {
                string textAfter = messageContent.Substring(lastIndex);
                if (!string.IsNullOrEmpty(textAfter)) contentParts.Add(new TextPart(textAfter));
            }

            if (!contentParts.Any(p => !(p is TextPart tp && string.IsNullOrWhiteSpace(tp.Text))) &&
                !string.IsNullOrWhiteSpace(messageContent))
            {
                _logger?.LogWarning("Multimodal parsing resulted in no valid parts, returning original content.");
                return new List<ContentPart> { new TextPart(messageContent) };
            }

            var combinedParts = new List<ContentPart>();
            StringBuilder currentText = null;
            foreach (var part in contentParts)
            {
                if (part is TextPart tp)
                {
                    if (currentText == null) currentText = new StringBuilder();
                    currentText.Append(tp.Text);
                }
                else
                {
                    if (currentText != null && currentText.Length > 0)
                    {
                        combinedParts.Add(new TextPart(currentText.ToString().Trim()));
                        currentText = null;
                    }

                    combinedParts.Add(part);
                }
            }

            if (currentText != null && currentText.Length > 0)
            {
                combinedParts.Add(new TextPart(currentText.ToString().Trim()));
            }

            return combinedParts.Where(p => !(p is TextPart tp && string.IsNullOrEmpty(tp.Text))).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during multimodal content parsing for content length: {ContentLength}",
                messageContent.Length);
            return new List<ContentPart> { new TextPart(messageContent) };
        }
    }

    private string GetProviderParameterName(string standardName, ModelType modelType)
    {
        if (modelType == ModelType.Gemini)
        {
            return standardName switch
            {
                "top_p" => "topP",
                "top_k" => "topK",
                "max_tokens" => "maxOutputTokens",
                "stop" => "stopSequences",
                _ => standardName
            };
        }

        if (modelType == ModelType.Anthropic)
        {
            return standardName switch
            {
                "stop" => "stop_sequences",
                "max_tokens" => "max_tokens",
                _ => standardName
            };
        }

        return standardName;
    }

    private bool IsParameterSupported(string providerParamName, ModelType modelType)
    {
        switch (modelType)
        {
            case ModelType.OpenAi:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "temperature", "top_p", "frequency_penalty", "presence_penalty", "max_tokens", "stop",
                    "seed", "response_format", "tools", "tool_choice", "logit_bias", "user", "n", "logprobs",
                    "top_logprobs"
                }.Contains(providerParamName);

            case ModelType.Anthropic:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "max_tokens", "temperature", "top_k", "top_p", "stop_sequences", "stream", "system", "messages",
                    "metadata", "model", "tools", "tool_choice"
                }.Contains(providerParamName);

            case ModelType.Gemini:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "temperature", "topP", "topK", "maxOutputTokens", "stopSequences", "candidateCount",
                    "response_mime_type", "response_schema",
                    "tools"
                }.Contains(providerParamName);

            case ModelType.DeepSeek:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "temperature", "top_p", "max_tokens", "stop", "frequency_penalty", "presence_penalty",
                    "logit_bias", "logprobs", "top_logprobs", "stream", "model", "messages", "n", "seed",
                    "response_format",
                    "enable_cot", "enable_reasoning", "reasoning_mode"
                }.Contains(providerParamName);

            default:
                _logger?.LogWarning("Parameter support check requested for unknown ModelType: {ModelType}", modelType);
                return false;
        }
    }

    private List<(string Role, string Content)> MergeConsecutiveRoles(List<(string Role, string Content)> messages)
    {
        if (messages == null || !messages.Any()) return new List<(string Role, string Content)>();

        var merged = new List<(string Role, string Content)>();
        var currentRole = messages[0].Role;
        var currentContent = new StringBuilder(messages[0].Content);

        for (int i = 1; i < messages.Count; i++)
        {
            if (messages[i].Role == currentRole)
            {
                if (currentContent.Length > 0 && !string.IsNullOrWhiteSpace(currentContent.ToString()))
                {
                    currentContent.AppendLine().AppendLine();
                }

                currentContent.Append(messages[i].Content);
            }
            else
            {
                merged.Add((currentRole, currentContent.ToString().Trim()));
                currentRole = messages[i].Role;
                currentContent.Clear().Append(messages[i].Content);
            }
        }

        merged.Add((currentRole, currentContent.ToString().Trim()));
        return merged.Where(m => !string.IsNullOrEmpty(m.Content)).ToList();
    }

    private void EnsureAlternatingRoles(List<object> messages, string userRole, string modelRole)
    {
        if (messages == null || !messages.Any()) return;

        string firstRole = GetRoleFromDynamicMessage(messages[0]);
        if (firstRole != userRole)
        {
            _logger?.LogError(
                "History for {ModelRole} provider does not start with a {UserRole} message. First role: {FirstRole}",
                modelRole, userRole, firstRole);
            
        }

        var cleanedMessages = new List<object>();
        if (messages.Count > 0)
        {
            cleanedMessages.Add(messages[0]); 
            for (int i = 1; i < messages.Count; i++)
            {
                string previousRole = GetRoleFromDynamicMessage(cleanedMessages.Last());
                string currentRole = GetRoleFromDynamicMessage(messages[i]);

                if (currentRole != previousRole)
                {
                    cleanedMessages.Add(messages[i]);
                }
                else
                {
                     _logger?.LogWarning("Found consecutive '{CurrentRole}' roles at index {Index}.", currentRole, i);

                    if (modelRole == "assistant" && currentRole == userRole)
                    {
                        _logger?.LogWarning(
                            "Inserting placeholder '{ModelRole}' message to fix consecutive '{UserRole}' roles for Anthropic.",
                            modelRole, userRole);

                        cleanedMessages.Add(new { role = modelRole, content = "..." });
                        cleanedMessages
                            .Add(messages[i]); 
                    }
                    else if (modelRole == "model" && currentRole == modelRole)
                    {
                        // Gemini: model -> model
                        _logger?.LogWarning("Merging consecutive '{ModelRole}' roles for Gemini.", modelRole);
                        // TODO: Implement content merging for Gemini consecutive model roles if needed
                        _logger?.LogError("Consecutive '{ModelRole}' role merging not implemented. Skipping message.",
                            modelRole);
                    }
                    else
                    {
                        _logger?.LogError(
                            "Skipping consecutive '{CurrentRole}' role at index {Index}. Original Message: {OriginalMessage}",
                            currentRole, i, TrySerialize(messages[i]));
                    }
                }
            }
        }

        messages.Clear();
        messages.AddRange(cleanedMessages);
    }

    private string GetRoleFromDynamicMessage(dynamic message)
    {
        try
        {
            if (message is IDictionary<string, object> dict && dict.TryGetValue("role", out var roleValue) &&
                roleValue is string roleStr) return roleStr;

            var roleProp = message.GetType().GetProperty("role");
            if (roleProp != null)
            {
                var value = roleProp.GetValue(message);
                if (value is string roleVal)
                {
                    return roleVal;
                }
            }

            if (message is System.Dynamic.ExpandoObject expando)
            {
                if (((IDictionary<string, object>)expando).TryGetValue("role", out var expandoRole) &&
                    expandoRole is string expandoRoleStr)
                {
                    return expandoRoleStr;
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger != null)
            {
                string typeName = message?.GetType().Name ?? "null";
                LoggerExtensions.LogWarning(_logger, ex,
                    "Could not determine role from dynamic message object of type {Type}", typeName);
            }
        }

        return string.Empty;
    }

    private string TrySerialize(object obj)
    {
        try
        {
            return JsonSerializer.Serialize(obj,
                new JsonSerializerOptions
                {
                    WriteIndented = false,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}]";
        }
    }

    private bool IsValidAnthropicImageType(string mimeType, out string normalizedMediaType)
    {
        normalizedMediaType = mimeType.ToLowerInvariant().Trim();
        var supported = new Dictionary<string, string>
        {
            { "image/jpeg", "image/jpeg" },
            { "image/png", "image/png" },
            { "image/gif", "image/gif" },
            { "image/webp", "image/webp" }
        };
        normalizedMediaType = supported.GetValueOrDefault(normalizedMediaType);
        return !string.IsNullOrEmpty(normalizedMediaType);
    }

    private bool IsValidGeminiImageType(string mimeType, out string normalizedMediaType)
    {
        string lowerMime = mimeType?.ToLowerInvariant() ?? "";
        var supported = new Dictionary<string, string>
        {
            { "image/png", "image/png" },
            { "image/jpeg", "image/jpeg" },
            { "image/webp", "image/webp" },
            { "image/heic", "image/heic" },
            { "image/heif", "image/heif" }
        };
        normalizedMediaType = supported.GetValueOrDefault(lowerMime);
        return !string.IsNullOrEmpty(normalizedMediaType);
    }

    private bool IsValidAnthropicDocumentType(string mimeType, out string normalizedMediaType)
    {
        normalizedMediaType = mimeType.ToLowerInvariant().Trim();
        var supported = new HashSet<string>
        {
            "application/pdf",
            "text/plain",
            "text/csv",
            "text/markdown",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/msword",
        };
        return supported.Contains(normalizedMediaType);
    }

    private List<object>? GetToolDefinitionsForPayload(ModelType modelType, List<Guid> activePluginIds)
    {
        if (activePluginIds == null || !activePluginIds.Any())
        {
            return null;
        }

        var allDefinitions = _pluginExecutorFactory.GetAllPluginDefinitions().ToList();
        if (!allDefinitions.Any())
        {
            _logger?.LogDebug("No plugin definitions found in the factory.");
            return null;
        }

        var activeDefinitions = allDefinitions
            .Where(def => activePluginIds.Contains(def.Id))
            .ToList();

        if (!activeDefinitions.Any())
        {
            _logger?.LogWarning("No matching definitions found in factory for active plugin IDs: {ActiveIds}",
                string.Join(", ", activePluginIds));
            return null;
        }

        _logger?.LogInformation("Found {DefinitionCount} active plugin definitions to format for {ModelType}.",
            activeDefinitions.Count, modelType);
        var formattedDefinitions = new List<object>();

        foreach (var def in activeDefinitions)
        {
            if (def.ParametersSchema == null)
            {
                _logger?.LogWarning(
                    "Skipping tool definition for {ToolName} ({ToolId}) due to missing parameter schema.", def.Name,
                    def.Id);
                continue;
            }

            try
            {
                // Adapt for specific provider formats
                switch (modelType)
                {
                    case ModelType.OpenAi:
                        formattedDefinitions.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = def.Name,
                                description = def.Description,
                                parameters = def.ParametersSchema
                            }
                        });
                        break;

                    case ModelType.Anthropic:
                        formattedDefinitions.Add(new
                        {
                            name = def.Name,
                            description = def.Description,
                            input_schema = def.ParametersSchema
                        });
                        break;

                    case ModelType.Gemini:
                        formattedDefinitions.Add(new
                        {
                            name = def.Name,
                            description = def.Description,
                            parameters = def.ParametersSchema
                        });
                        break;

                    default:
                        _logger?.LogWarning(
                            "Tool definition requested for provider {ModelType} which may not support the standard format. Skipping tool: {ToolName}",
                            modelType, def.Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Error formatting tool definition for {ToolName} ({ToolId}) for provider {ModelType}", def.Name,
                    def.Id, modelType);
            }
        }

        if (!formattedDefinitions.Any())
        {
            _logger?.LogWarning("No tool definitions could be formatted successfully for {ModelType}.", modelType);
            return null;
        }

        _logger?.LogInformation("Successfully formatted {FormattedCount} tool definitions for {ModelType}.",
            formattedDefinitions.Count, modelType);

        return formattedDefinitions;
    }
}