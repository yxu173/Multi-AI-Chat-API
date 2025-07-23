using System.Runtime.CompilerServices;
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

public class DeepSeekService : BaseAiService
{
    private const string DeepSeekBaseUrl = "https://api.deepseek.com/v1/";
    private readonly ILogger<DeepSeekService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly MultimodalContentParser _multimodalContentParser;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.DeepSeekService", "1.0.0");

    protected override string ProviderName => "DeepSeek";

    public DeepSeekService(
        HttpClient httpClient,
        string? apiKey,
        string modelCode,
        ILogger<DeepSeekService> logger,
        IResilienceService resilienceService,
        IStreamChunkParser chunkParser,
        MultimodalContentParser multimodalContentParser)
        : base(httpClient, apiKey, modelCode, DeepSeekBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        ConfigureHttpClient();
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            activity?.SetTag("auth.method", "Bearer");
        }
        else
        {
            activity?.AddEvent(new ActivityEvent("API key not configured."));
            _logger.LogWarning("DeepSeek API key is not configured. Requests may fail.");
        }
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting DeepSeek tool result for ToolCallId {ToolCallId}, ToolName {ToolName}", context.ToolCallId, context.ToolName);

        var messagePayload = new
        {
            role = "tool",
            tool_call_id = context.ToolCallId,
            content = context.Result
        };

        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        var messageDto = new MessageDto(contentJson, false, Guid.NewGuid());

        return Task.FromResult(messageDto);
    }

    protected override string GetEndpointPath() => "chat/completions";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) 
            {
                activity?.AddEvent(new ActivityEvent("Stream reading cancelled."));
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
            {
                if (!string.IsNullOrWhiteSpace(line)) 
                {
                    activity?.AddEvent(new ActivityEvent("Skipped non-data line in stream", tags: new ActivityTagsCollection { { "line_preview", line.Substring(0, Math.Min(line.Length, 100)) } }));
                }
                continue;
            }
            
            var jsonData = line.Substring("data:".Length).Trim();
            if (jsonData.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                activity?.AddEvent(new ActivityEvent("Received [DONE] marker."));
                break;
            }

            if (!string.IsNullOrEmpty(jsonData))
            {
                activity?.AddEvent(new ActivityEvent("Yielding data chunk"));
                yield return jsonData;
            }
        }
        activity?.AddEvent(new ActivityEvent("Finished reading stream."));
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
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (DeepSeekBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (DeepSeekBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (DeepSeekBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (DeepSeekBaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri;
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing cancelled by token.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
                    break;
                }

                if (string.IsNullOrWhiteSpace(jsonChunk)) continue;

                var parsedChunk = ChunkParser.ParseChunk(jsonChunk);
                yield return parsedChunk;
                
                if (parsedChunk.FinishReason is not null)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing completed due to finish_reason."));
                    break;
                }
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Stream completed.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    // --- Begin merged payload builder logic ---

    public override async Task<AiRequestPayload> BuildPayloadAsync(
        AiRequestContext context,
        List<PluginDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;
        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;
        AddParameters(requestObj, context);
        var processedMessages = await ProcessMessagesForDeepSeekAsync(context, cancellationToken);
        requestObj["messages"] = processedMessages;
        if (tools?.Any() == true)
        {
            var formattedTools = tools.Select(def => new
            {
                type = "function",
                function = new
                {
                    name = def.Name,
                    description = def.Description,
                    parameters = def.ParametersSchema
                }
            }).ToList();
            requestObj["tools"] = formattedTools;
            requestObj["tool_choice"] = "auto";
            _logger?.LogInformation("Adding {ToolCount} tool definitions to DeepSeek payload for model {ModelCode}", tools.Count, model.ModelCode);
        }
        CustomizePayload(requestObj, context);
        return new AiRequestPayload(requestObj);
    }

    private async Task<List<object>> ProcessMessagesForDeepSeekAsync(AiRequestContext context, CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();
        string? systemMessage = context.AiAgent?.ModelParameter.SystemInstructions ?? context.UserSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
        }
        var mergedHistory = MergeConsecutiveRoles(
            context.History.Select(m => (m.IsFromAi ? "assistant" : "user", m.Content?.Trim() ?? "")).ToList());
        foreach (var (role, rawContent) in mergedHistory)
        {
            if (string.IsNullOrEmpty(rawContent)) continue;
            var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
            var processedParts = new List<string>();
            foreach (var part in contentParts)
            {
                string partText = "";
                if (part is TextPart tp)
                {
                    partText = tp.Text;
                }
                else if (part is ImagePart ip)
                {
                    partText = $"[Image: {ip.FileName ?? ip.MimeType} - Not Sent]";
                }
                else if (part is FilePart fp)
                {
                    if (fp.MimeType == "text/csv" || fp.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger?.LogWarning("CSV file {FileName} detected - DeepSeek doesn't support CSV files directly. Using the csv_reader plugin is recommended instead.", fp.FileName);
                        partText = $"Note: The CSV file '{fp.FileName}' can't be processed directly by DeepSeek. Please use the csv_reader tool to analyze this file. Example usage:\ncsv_reader(file_name=\"{fp.FileName}\", max_rows=100, analyze=true)";
                    }
                    else
                    {
                        partText = $"[File: {fp.FileName} ({fp.MimeType}) - Not Sent]";
                    }
                }
                if (!string.IsNullOrEmpty(partText))
                {
                    processedParts.Add(partText);
                }
            }
            string contentText = string.Join("\n", processedParts).Trim();
            if (!string.IsNullOrWhiteSpace(contentText))
            {
                processedMessages.Add(new { role, content = contentText });
            }
        }
        bool isReasonerModel = context.SpecificModel.ModelCode?.ToLower().Contains("reasoner") ?? false;
        if (isReasonerModel && processedMessages.Count > 0)
        {
            int firstNonSystemIndex = processedMessages.FindIndex(m => GetRoleFromDynamicMessage(m) != "system");
            if (firstNonSystemIndex == -1 || GetRoleFromDynamicMessage(processedMessages[firstNonSystemIndex]) != "user")
            {
                int insertIndex = firstNonSystemIndex == -1 ? processedMessages.Count : firstNonSystemIndex;
                processedMessages.Insert(insertIndex, new { role = "user", content = "Proceed." });
                _logger?.LogWarning("Inserted placeholder user message for DeepSeek reasoner model {ModelCode} as the first non-system message was not 'user'.", context.SpecificModel.ModelCode);
            }
        }
        return processedMessages;
    }

    private void AddParameters(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        var parameters = new Dictionary<string, object>();
        var model = context.SpecificModel;
        var agent = context.AiAgent;
        var userSettings = context.UserSettings;
        if (agent?.AssignCustomModelParameters == true && agent.ModelParameter != null)
        {
            var sourceParams = agent.ModelParameter;
            parameters["temperature"] = sourceParams.Temperature;
            parameters["max_tokens"] = sourceParams.MaxTokens;
        }
        else if (userSettings != null)
        {
            parameters["temperature"] = userSettings.ModelParameters.Temperature;
        }
        if (!parameters.ContainsKey("max_tokens") && model.MaxOutputTokens.HasValue)
        {
            parameters["max_tokens"] = model.MaxOutputTokens.Value;
        }
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
        var currentContent = new System.Text.StringBuilder(messages[0].Content);
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

    private string GetRoleFromDynamicMessage(dynamic message)
    {
        try
        {
            if (message is IDictionary<string, object> dict && dict.TryGetValue("role", out var roleValue) && roleValue is string roleStr)
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

    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        if (useThinking)
        {
            if (!requestObj.ContainsKey("enable_cot"))
            {
                requestObj["enable_cot"] = true;
                _logger?.LogDebug("Enabled DeepSeek 'enable_cot' parameter for model {ModelCode}", context.SpecificModel.ModelCode);
            }
            if (!requestObj.ContainsKey("enable_reasoning"))
            {
                requestObj["enable_reasoning"] = true;
                _logger?.LogDebug("Enabled DeepSeek 'enable_reasoning' parameter for model {ModelCode}", context.SpecificModel.ModelCode);
            }
            if (!requestObj.ContainsKey("reasoning_mode"))
            {
                requestObj["reasoning_mode"] = "detailed";
                _logger?.LogDebug("Set DeepSeek 'reasoning_mode' to 'detailed' for thinking");
            }
        }
    }
}