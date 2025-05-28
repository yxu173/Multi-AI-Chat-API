using System.Runtime.CompilerServices;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;

namespace Infrastructure.Services.AiProvidersServices;

public class DeepSeekService : BaseAiService
{
    private const string BaseUrl = "https://api.deepseek.com/v1/";
    private readonly ILogger<DeepSeekService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    public DeepSeekService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey,
        string modelCode,
        ILogger<DeepSeekService> logger,
        IResilienceService resilienceService)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName)
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override string ProviderName => "DeepSeek";

    protected override void ConfigureHttpClient()
    {
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        }
        else
        {
            _logger.LogWarning("DeepSeek API key is not configured. Requests may fail.");
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
            _logger.LogDebug("Successfully received stream response header from {ProviderName} model {ModelCode}", ProviderName, ModelCode);
            initialRequestSuccess = true;
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            yield break; 
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw; 
        }

        if (!initialRequestSuccess || response == null) 
        {
            response?.Dispose();
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri; // For logging
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) 
                {
                    _logger.LogInformation("{ProviderName} stream processing cancelled by token for model {ModelCode}. URI: {SuccessfulRequestUri}", ProviderName, ModelCode, successfulRequestUri);
                    break;
                }

                bool isCompletion = false;
                try
                {
                    using var doc = JsonDocument.Parse(jsonChunk);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && 
                        choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    { 
                        if(choices[0].TryGetProperty("finish_reason", out var finishReason) && 
                           finishReason.ValueKind != JsonValueKind.Null && 
                           finishReason.ValueKind != JsonValueKind.Undefined && 
                           !string.IsNullOrEmpty(finishReason.GetString()))
                        {
                            _logger.LogDebug("{ProviderName} stream indicates completion. Finish reason: {FinishReason}. URI: {SuccessfulRequestUri}", ProviderName, finishReason.GetString(), successfulRequestUri);
                            isCompletion = true;
                        }
                    }
                }
                catch (JsonException jsonEx) 
                { 
                    _logger.LogWarning(jsonEx, "Failed to parse JSON chunk from {ProviderName}: {JsonChunk}. URI: {SuccessfulRequestUri}", ProviderName, jsonChunk, successfulRequestUri);
                }

                yield return new AiRawStreamChunk(jsonChunk, isCompletion);
                if (isCompletion) 
                {
                    _logger.LogDebug("{ProviderName} stream processing completed due to finish_reason for model {ModelCode}. URI: {SuccessfulRequestUri}", ProviderName, ModelCode, successfulRequestUri);
                    break;
                }
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("{ProviderName} stream completed for model {ModelCode}. URI: {SuccessfulRequestUri}", ProviderName, ModelCode, successfulRequestUri);
            }
        }
        finally
        {
            response.Dispose(); 
        }
    }
}