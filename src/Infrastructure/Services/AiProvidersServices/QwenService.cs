using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Headers;

namespace Infrastructure.Services.AiProvidersServices;

public class QwenService : BaseAiService
{
    private const string QwenBaseUrl = "https://api.aimlapi.com/v1/";
    private readonly ILogger<QwenService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    public QwenService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey, 
        string modelCode, 
        ILogger<QwenService> logger,
        IResilienceService resilienceService)
        : base(httpClientFactory, apiKey, modelCode, QwenBaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName)
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override string ProviderName => "Qwen";

    protected override void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            _logger.LogWarning("Qwen API key is not configured. Requests will likely fail.");
        }
        else
        {
            HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        }
    }

    protected override string GetEndpointPath() => "chat/completions";

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        HttpResponseMessage? response = null;
        bool initialRequestSuccess = false;
        Uri? requestUriForLogging = null;        
        
        try
        {
            _logger.LogInformation("Preparing to send request to {ProviderName} model {ModelCode} with API Key ID (if managed): {ApiKeyId} using resilience pipeline", 
                ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");
            
            response = await _resiliencePipeline.ExecuteAsync(
                async ct => 
                {
                    var attemptRequest = CreateRequest(requestPayload);
                    if (!attemptRequest.Headers.Accept.Any(h => h.MediaType == "text/event-stream"))
                    {
                        attemptRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    }
                    requestUriForLogging = attemptRequest.RequestUri;
                    _logger.LogDebug("Attempting to send request to {ProviderName} endpoint: {Endpoint} via Polly pipeline", ProviderName, requestUriForLogging);
                    return await HttpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break; 
            }
            _logger.LogDebug("Successfully received stream response header from {ProviderName} model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, response.RequestMessage?.RequestUri);
            initialRequestSuccess = true;
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
             _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", 
                ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
             yield break; 
        }
        catch (Exception ex) 
        {
             _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", 
                ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (QwenBaseUrl + GetEndpointPath()));
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