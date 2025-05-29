using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Diagnostics;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : BaseAiService
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1/";
    private readonly ILogger<OpenAiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.OpenAiService", "1.0.0");

    protected override string ProviderName => "OpenAI";

    public OpenAiService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey, 
        string modelCode, 
        ILogger<OpenAiService> logger,
        IResilienceService resilienceService)
        : base(httpClientFactory, apiKey, modelCode, OpenAiBaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName) 
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        HttpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
        }
        HttpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");
    }

    protected override string GetEndpointPath()
    {
        _logger.LogWarning("OpenAI GetEndpointPath is returning 'responses'. Verify this is correct for the intended OpenAI API endpoint.");
        return "responses";
    }

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

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("event:"))
            {
                continue;
            }
            
            if (line.StartsWith("data:"))
            {
                var jsonData = line["data:".Length..].Trim();
                
                if (!string.IsNullOrEmpty(jsonData))
                {
                    activity?.AddEvent(new ActivityEvent("Yielding data chunk"));
                    yield return jsonData;
                }
            }
        }
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
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw; 
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw; 
        }
        
        if (!initialRequestSuccess || response == null) 
        {
            response?.Dispose();
            yield break;
        }

        try
        {
            var successfulRequestUri = response.RequestMessage?.RequestUri;
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return new AiRawStreamChunk(jsonChunk);
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Finished reading stream successfully."));
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}