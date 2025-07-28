using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Diagnostics;
using Application.Services.Messaging;
using Application.Services.Helpers;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : BaseAiService
{
    private const string AnthropicBaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicApiVersion = "2023-06-01";
    private readonly ILogger<AnthropicService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly MultimodalContentParser _multimodalContentParser;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.AnthropicService", "1.0.0");

    protected override string ProviderName => "Anthropic";

    public AnthropicService(
        HttpClient httpClient,
        string? apiKey, 
        string modelCode, 
        ILogger<AnthropicService> logger,
        IResilienceService resilienceService,
        IStreamChunkParser chunkParser,
        MultimodalContentParser multimodalContentParser)
        : base(httpClient, apiKey, modelCode, AnthropicBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        ConfigureHttpClient();
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        HttpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        }
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicApiVersion);
        //HttpClient.DefaultRequestHeaders.Add("anthropic-beta", "mcp-client-2025-04-04");
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting Anthropic tool result for ToolCallId {ToolCallId}, ToolName {ToolName}", context.ToolCallId, context.ToolName);

        var messagePayload = new
        {
            role = "user",
            content = new[]
            {
                new
                {
                    type = "tool_result",
                    tool_use_id = context.ToolCallId,
                    content = context.Result,
                    is_error = !context.WasSuccessful
                }
            }
        };
        
        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        var messageDto = new MessageDto(contentJson, false, Guid.NewGuid());
        
        return Task.FromResult(messageDto);
    }

    protected override string GetEndpointPath() => "messages";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? currentEvent = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Stream reading cancelled."));
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event: "))
            {
                currentEvent = line.Substring("event: ".Length).Trim();
                activity?.AddEvent(new ActivityEvent("Anthropic stream event", tags: new ActivityTagsCollection { { "anthropic.event_type", currentEvent } }));
            }
            else if (line.StartsWith("data: "))
            {
                var jsonData = line.Substring("data: ".Length).Trim();
                if (!string.IsNullOrWhiteSpace(jsonData))
                {
                    activity?.AddEvent(new ActivityEvent("Anthropic stream data received", tags: new ActivityTagsCollection { { "anthropic.event_type", currentEvent ?? "unknown" } }));
                    yield return jsonData;
                }
            }
            else
            {
                activity?.AddEvent(new ActivityEvent("Anthropic stream ignored line", tags: new ActivityTagsCollection { { "line_content_preview", line.Substring(0, Math.Min(line.Length, 50)) } }));
            }
        }
        if (cancellationToken.IsCancellationRequested)
        {
            activity?.AddEvent(new ActivityEvent("Finished reading Anthropic stream due to cancellation."));
        }
        else
        {
            activity?.AddEvent(new ActivityEvent("Finished reading Anthropic stream (end of stream)."));
        }
    }

    public override async IAsyncEnumerable<ParsedChunkInfo> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        using var activity = ActivitySource.StartActivity(nameof(StreamResponseAsync));
        activity?.SetTag("ai.provider", ProviderName);
        activity?.SetTag("ai.model", ModelCode);
        activity?.SetTag("ai.provider_api_key_id", providerApiKeyId?.ToString());

        HttpResponseMessage? response = null;
        Uri? requestUriForLogging = null;

        try
        {
            response = await _resiliencePipeline.ExecuteAsync(
                async ct =>
                {
                    using var attemptActivity = ActivitySource.StartActivity("SendHttpRequestAttempt");
                    var attemptRequest = CreateRequest(requestPayload);
                    requestUriForLogging = attemptRequest.RequestUri;
                    attemptActivity?.SetTag("http.url", requestUriForLogging?.ToString());
                    attemptActivity?.SetTag("http.method", attemptRequest.Method.ToString());
                    return await HttpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken).ConfigureAwait(false);

            activity?.SetTag("http.response_status_code", ((int)response.StatusCode).ToString());

            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"API call failed with status {response.StatusCode}");
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri;
        activity?.AddEvent(new ActivityEvent("Anthropic request successful, beginning to process stream.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() }, { "http.status_code", response.StatusCode.ToString()} }));
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(jsonChunk)) continue;

                var parsedChunk = ChunkParser.ParseChunk(jsonChunk);
                yield return parsedChunk;

                if (parsedChunk.FinishReason is not null)
                {
                    activity?.AddEvent(new ActivityEvent("Anthropic stream finished.", tags: new ActivityTagsCollection { { "finish_reason", parsedChunk.FinishReason } }));
                    break;
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Finished reading Anthropic stream successfully."));
            }
        }
        finally
        {
            response.Dispose();
        }
    }
    public override async Task<AiRequestPayload> BuildPayloadAsync(AiRequestContext context, List<PluginDefinition>? tools = null, CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;

        AddParameters(requestObj, context);

        var (systemPrompt, processedMessages) = await ProcessMessagesForAnthropicAsync(context.History, context, cancellationToken);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            requestObj["system"] = systemPrompt;
        }
        requestObj["messages"] = processedMessages;

        if (tools?.Any() == true) 
        {
            _logger?.LogInformation("Adding {ToolCount} tool definitions to Anthropic payload for model {ModelCode}",
                tools.Count, model.ModelCode);
            var formattedTools = tools.Select(def => new
            {
                name = def.Name,
                description = def.Description,
                input_schema = def.ParametersSchema
            }).ToList();
            requestObj["tools"] = formattedTools;
            requestObj["tool_choice"] = new { type = "auto" };
        }

        CustomizePayload(requestObj, context);

        return new AiRequestPayload(requestObj);
    }

    private async Task<(string? SystemPrompt, List<object> Messages)> ProcessMessagesForAnthropicAsync(List<MessageDto> history, AiRequestContext context, CancellationToken cancellationToken)
    {
        string? agentSystemMessage = context.AiAgent?.ModelParameter.SystemInstructions;
        string? userSystemMessage = context.UserSettings?.ModelParameters.SystemInstructions;
        string? finalSystemPrompt = agentSystemMessage ?? userSystemMessage;

        var otherMessages = new List<object>();
        var mergedHistory = MergeConsecutiveRoles( 
            history.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());

        foreach (var (role, rawContent) in mergedHistory)
        {
            string anthropicRole = role; 
            if (string.IsNullOrEmpty(rawContent)) continue;

            var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
            if (contentParts.Count > 1 || contentParts.Any(p => p is not TextPart))
            {
                var anthropicContentItems = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart tp: anthropicContentItems.Add(new { type = "text", text = tp.Text }); break;
                        case ImagePart ip:
                            if (Validators.IsValidAnthropicImageType(ip.MimeType, out var mediaType)) 
                            {
                                try
                                {
                                    var (width, height) = Application.Services.Helpers.ImageHelper.GetImageDimensions(ip.Base64Data);
                                    if (width > 8000 || height > 8000)
                                    {
                                        _logger?.LogWarning("Image {FileName} exceeds Anthropic's max dimension (8000px). Skipping image.", ip.FileName);
                                        anthropicContentItems.Add(new { type = "text", text = $"[Image: {ip.FileName ?? ip.MimeType} - Skipped: Exceeds 8000px dimension limit]" });
                                    }
                                    else
                                    {
                                        anthropicContentItems.Add(new
                                        {
                                            type = "image",
                                            source = new { type = "base64", media_type = mediaType, data = ip.Base64Data }
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "Failed to get image dimensions for {FileName}. Skipping image.", ip.FileName);
                                    anthropicContentItems.Add(new { type = "text", text = $"[Image: {ip.FileName ?? ip.MimeType} - Skipped: Unable to read image dimensions]" });
                                }
                            }
                            else
                            {
                                _logger?.LogWarning("Unsupported image type '{MimeType}' for Anthropic. Sending placeholder text.", ip.MimeType);
                                anthropicContentItems.Add(new { type = "text", text = $"[Image: {ip.FileName ?? ip.MimeType} - Unsupported Type]" });
                            }
                            break;
                        case FilePart fp:
                            if (fp.MimeType == "text/csv" || fp.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                _logger?.LogWarning("CSV file {FileName} detected - Anthropic doesn't support CSV files directly. " +
                                    "Using the csv_reader plugin is recommended instead.", fp.FileName);
                                anthropicContentItems.Add(new { 
                                    type = "text", 
                                    text = $"Note: The CSV file '{fp.FileName}' can't be processed directly by Anthropic. " +
                                           $"Please use the csv_reader tool to analyze this file. Example usage:\n\n" +
                                           $"{{\n  \"name\": \"csv_reader\",\n  \"input\": {{\n    \"file_name\": \"{fp.FileName}\",\n    \"max_rows\": 100,\n    \"analyze\": true\n  }}\n}}" 
                                });
                            }
                            else if (Validators.IsValidAnthropicDocumentType(fp.MimeType, out var docMediaType))
                            {
                                _logger?.LogInformation("Adding document {FileName} ({MediaType}) to Anthropic message using 'document' type.", fp.FileName, docMediaType);
                                var mediaTypeValue = docMediaType;
                                var dataValue = fp.Base64Data;
                                anthropicContentItems.Add(new
                                {
                                    type = "document",
                                    source = new { type = "base64", media_type = mediaTypeValue, data = dataValue }
                                });
                            }
                            else
                            {
                                _logger?.LogWarning("Document type '{MimeType}' is not listed as supported by Anthropic. Sending placeholder text for file {FileName}.", fp.MimeType, fp.FileName);
                                anthropicContentItems.Add(new { type = "text", text = $"[Document Attached: {fp.FileName} - Unsupported Type ({fp.MimeType}) for direct API processing]" });
                            }
                            break;
                    }
                }
                if (anthropicContentItems.Any()) otherMessages.Add(new { role = anthropicRole, content = anthropicContentItems.ToArray() });
            }
            else if (contentParts.Count == 1 && contentParts[0] is TextPart singleTextPart)
            {
                otherMessages.Add(new { role = anthropicRole, content = singleTextPart.Text });
            }
        }

        EnsureAlternatingRoles(otherMessages, "user", "assistant"); 
        return (finalSystemPrompt?.Trim(), otherMessages);
    }

    private void AddParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        
        parameters["temperature"] = context.Temperature;
        parameters["max_tokens"] = context.OutputToken;
      
        foreach (var kvp in parameters)
        {
            string standardName = kvp.Key;
            string providerName = standardName;
            requestObj[providerName] = kvp.Value;
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
                "History for {ModelRole} provider does not start with a {UserRole} message. First role: {FirstRole}. This might cause API errors.",
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
                    _logger?.LogWarning(
                        "Found consecutive '{CurrentRole}' roles at index {Index} for {ModelRole} provider.",
                        currentRole, i, modelRole);
                    bool handled = false;
                    if (modelRole == "assistant" && currentRole == userRole)
                    {
                        _logger?.LogWarning(
                            "Injecting placeholder '{ModelRole}' message to fix consecutive '{UserRole}' roles for Anthropic.",
                            modelRole, userRole);
                        cleanedMessages.Add(new { role = modelRole, content = "..." });
                        cleanedMessages.Add(messages[i]);
                        handled = true;
                    }
                    if (!handled)
                    {
                        _logger?.LogError(
                            "Unhandled consecutive '{CurrentRole}' role at index {Index} for {ModelRole}. Skipping message to avoid potential API error. Original Message: {OriginalMessage}",
                            currentRole, i, modelRole, TrySerialize(messages[i]));
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
                roleValue is string roleStr)
            {
                return roleStr;
            }
            var roleProp = message.GetType().GetProperty("role");
            if (roleProp != null && roleProp.PropertyType == typeof(string))
            {
                var value = roleProp.GetValue(message);
                if (value is string roleVal)
                {
                    return roleVal;
                }
            }
        }
        catch (Exception) { }
        return string.Empty;
    }

    private string TrySerialize(object obj)
    {
        try
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            });
        }
        catch (Exception ex)
        {
            return $"[Serialization Error: {ex.Message}] - Type: {obj?.GetType().Name ?? "null"}";
        }
    }

    private static class Validators
    {
        private static readonly Dictionary<string, string> AnthropicSupportedImageTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "image/jpeg", "image/jpeg" },
                { "image/png", "image/png" },
                { "image/gif", "image/gif" },
                { "image/webp", "image/webp" }
            };
        private static readonly HashSet<string> AnthropicSupportedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "text/plain",
            "text/csv",
            "text/markdown",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/msword"
        };
        public static bool IsValidAnthropicImageType(string mimeType, out string normalizedMediaType)
        {
            normalizedMediaType = mimeType?.ToLowerInvariant().Trim() ?? string.Empty;
            normalizedMediaType = AnthropicSupportedImageTypes.GetValueOrDefault(normalizedMediaType, string.Empty);
            return !string.IsNullOrEmpty(normalizedMediaType);
        }
        public static bool IsValidAnthropicDocumentType(string mimeType, out string normalizedMediaType)
        {
            normalizedMediaType = mimeType?.ToLowerInvariant().Trim() ?? string.Empty;
            return AnthropicSupportedDocumentTypes.Contains(normalizedMediaType);
        }
    }

    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
         if (!requestObj.ContainsKey("max_tokens"))
        {
            if (requestObj.TryGetValue("max_output_tokens", out var maxOutputTokens))
            {
                requestObj["max_tokens"] = maxOutputTokens;
                _logger?.LogDebug("Mapped 'max_output_tokens' to 'max_tokens' for Anthropic");
            }
        }
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        if (useThinking)
        {
            const int defaultThinkingBudget = 1024;
            requestObj["thinking"] = new { type = "enabled", budget_tokens = defaultThinkingBudget };
            requestObj["temperature"] = 1.0;
            requestObj.Remove("top_k");
            requestObj.Remove("top_p");
            _logger?.LogDebug("Enabled Anthropic native 'thinking' parameter with budget {Budget}", defaultThinkingBudget);
        }
    }
}