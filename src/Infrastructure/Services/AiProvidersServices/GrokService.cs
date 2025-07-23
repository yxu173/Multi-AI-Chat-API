using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using Application.Services.Messaging;
using Application.Services.Helpers;

namespace Infrastructure.Services.AiProvidersServices;

public class GrokService : BaseAiService
{
    private const string GrokBaseUrl = "https://api.x.ai/v1/";
    private readonly ILogger<GrokService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly MultimodalContentParser _multimodalContentParser;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.GrokService", "1.0.0");

    protected override string ProviderName => "Grok";

    public GrokService(
        HttpClient httpClient,
        string? apiKey,
        string modelCode,
        ILogger<GrokService> logger,
        IResilienceService resilienceService,
        IStreamChunkParser chunkParser,
        MultimodalContentParser multimodalContentParser)
        : base(httpClient, apiKey, modelCode, GrokBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        _multimodalContentParser = multimodalContentParser ?? throw new ArgumentNullException(nameof(multimodalContentParser));
        ConfigureHttpClient();
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            activity?.AddEvent(new ActivityEvent("API key not configured."));
            _logger.LogWarning("Grok API key is not configured. Requests will likely fail.");
        }
        else
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            activity?.SetTag("auth.method", "Bearer");
        }
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting Grok tool result for ToolCallId {ToolCallId}, ToolName {ToolName}", context.ToolCallId, context.ToolName);

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
                _logger.LogInformation("Grok raw chunk: {JsonData}", jsonData);
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
                    if (!attemptRequest.Headers.Accept.Any(h => h.MediaType == "text/event-stream"))
                    {
                        attemptRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    }
                    requestUriForLogging = attemptRequest.RequestUri;
                    attemptActivity?.SetTag("http.url", requestUriForLogging?.ToString());
                    attemptActivity?.SetTag("http.method", attemptRequest.Method.ToString());
                    return await HttpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken).ConfigureAwait(false);

            activity?.SetTag("http.response_status_code", ((int)response.StatusCode).ToString());

            // Log response headers to see if usage information is available
            _logger.LogInformation("Grok response headers:");
            foreach (var header in response.Headers)
            {
                _logger.LogInformation("Header: {HeaderName} = {HeaderValue}", header.Key, string.Join(", ", header.Value));
            }

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
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (GrokBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (GrokBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}",
               ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (GrokBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}",
               ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (GrokBaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) 
                {
                    _logger.LogInformation("{ProviderName} stream processing cancelled by token for model {ModelCode}.", ProviderName, ModelCode);
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
                 _logger.LogDebug("{ProviderName} stream completed for model {ModelCode}.", ProviderName, ModelCode);
            }
        }
        finally
        {
            response.Dispose(); 
        }
    }

    // --- Begin merged payload builder logic ---

    public override async Task<AiRequestPayload> BuildPayloadAsync(AiRequestContext context, List<PluginDefinition>? tools = null, CancellationToken cancellationToken = default)
    {
        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;
        requestObj["model"] = model.ModelCode;
        requestObj["stream"] = true;
        AddParameters(requestObj, context);
        CustomizePayload(requestObj, context);
        var processedMessages = await ProcessMessagesForGrokInputAsync(context.History, context.AiAgent, context.UserSettings, cancellationToken);
        requestObj["messages"] = processedMessages;
        if (tools != null && tools.Any())
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

    private async Task<List<object>> ProcessMessagesForGrokInputAsync(
        List<MessageDto> history,
        Domain.Aggregates.AiAgents.AiAgent? aiAgent,
        Domain.Aggregates.Users.UserAiModelSettings? userSettings,
        CancellationToken cancellationToken)
    {
        var processedMessages = new List<object>();
        string? systemMessage = aiAgent?.ModelParameter.SystemInstructions ?? userSettings?.ModelParameters.SystemInstructions;
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            processedMessages.Add(new { role = "system", content = systemMessage.Trim() });
            _logger?.LogDebug("Adding system instructions as a message for Grok.");
        }
        foreach (var message in history)
        {
            var role = message.IsFromAi ? "system" : "user";
            var rawContent = message.Content?.Trim() ?? "";
            if (string.IsNullOrEmpty(rawContent)) continue;
            if (!message.IsFromAi)
            {
                var contentParts = await _multimodalContentParser.ParseAsync(rawContent, cancellationToken);
                var contentArray = new List<object>();
                foreach (var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            var txt = textPart.Text?.Trim();
                            if (!string.IsNullOrEmpty(txt))
                                contentArray.Add(new { type = "text", text = txt });
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
                        case FilePart filePart:
                            if (filePart.MimeType == "text/csv" || filePart.FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                _logger?.LogWarning("CSV file {FileName} detected - Grok doesn't support CSV files directly. Using the csv_reader plugin is recommended instead.", filePart.FileName);
                                contentArray.Add(new { type = "text", text = $"Note: The CSV file '{filePart.FileName}' can't be processed directly by Grok. Please use the csv_reader tool to analyze this file. Example usage:\n\n{{\n  \"name\": \"csv_reader\",\n  \"arguments\": {{\n    \"file_name\": \"{filePart.FileName}\",\n    \"max_rows\": 100,\n    \"analyze\": true\n  }}\n}}" });
                            }
                            else
                            {
                                contentArray.Add(new { type = "text", text = $"[Attached file: {filePart.FileName} ({filePart.MimeType}) - Not supported by Grok directly]" });
                            }
                            break;
                    }
                }
                if (contentArray.Count > 0)
                {
                    processedMessages.Add(new { role = role, content = contentArray });
                }
            }
            else
            {
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
                                }
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
                    processedMessages.Add(new { role = "assistant", content = rawContent });
                }
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

    private void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        requestObj["temperature"] = 0.0;
        bool useThinking = context.RequestSpecificThinking == true || context.SpecificModel.SupportsThinking;
        if (useThinking)
        {
            requestObj["reasoning_effort"] = "high";
            _logger?.LogDebug("Set Grok 'reasoning_effort' to 'high' for thinking mode");
        }
    }
} 