using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : BaseAiService
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1/";
    private readonly ILogger<OpenAiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

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
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) break;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("event:"))
            {
                continue;
            }
            
            if (line.StartsWith("data:"))
            {
                var jsonData = line["data:".Length..].Trim();
                
                if (!string.IsNullOrEmpty(jsonData))
                {
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
        HttpResponseMessage? response = null;
        bool initialRequestSuccess = false;
        Uri? requestUriForLogging = null;

        try
        {
            response = await _resiliencePipeline.ExecuteAsync(
                async ct => 
                {
                    var attemptRequest = CreateRequest(requestPayload); 
                    requestUriForLogging = attemptRequest.RequestUri;
                    _logger.LogDebug("Attempting to send request to OpenAI endpoint: {Endpoint} via Polly pipeline", requestUriForLogging);
                    return await HttpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }
            _logger.LogDebug("Successfully received stream response header from OpenAI model {ModelCode}", ModelCode);
            initialRequestSuccess = true;
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw; 
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (OpenAiBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
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
                _logger.LogDebug("Finished reading OpenAI stream for request to {Endpoint}", successfulRequestUri);
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}