using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Headers;
using System.Diagnostics;

namespace Infrastructure.Services.AiProvidersServices;

public class GrokService : BaseAiService
{
    private const string GrokBaseUrl = "https://api.x.ai/v1/";
    private readonly ILogger<GrokService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.GrokService", "1.0.0");

    public GrokService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey, 
        string modelCode,
        ILogger<GrokService> logger,
        IResilienceService resilienceService)
        : base(httpClientFactory, apiKey, modelCode, GrokBaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName)
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override string ProviderName => "Grok";

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
            HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            activity?.SetTag("auth.method", "Bearer");
        }
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

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        using var activity = ActivitySource.StartActivity(nameof(StreamResponseAsync));
        activity?.SetTag("ai.provider", ProviderName);
        activity?.SetTag("ai.model", ModelCode);
        activity?.SetTag("ai.provider_api_key_id", providerApiKeyId?.ToString());

        HttpResponseMessage? response = null;
        bool initialRequestSuccess = false;
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

            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"API call failed with status {response.StatusCode}");
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }
            initialRequestSuccess = true;
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

        if (!initialRequestSuccess || response == null)
        {
            response?.Dispose();
            yield break;
        }

        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                                .WithCancellation(cancellationToken)
                                .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) 
                {
                    _logger.LogInformation("{ProviderName} stream processing cancelled by token for model {ModelCode}.", ProviderName, ModelCode);
                    break;
                }
                yield return new AiRawStreamChunk(jsonChunk);
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
} 