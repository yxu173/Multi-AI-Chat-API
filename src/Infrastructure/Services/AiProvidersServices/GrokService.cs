using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.AiProvidersServices;

public class GrokService : BaseAiService
{
    private const string GrokBaseUrl = "https://api.x.ai/v1/";
    private readonly ILogger<GrokService> _logger;

    public GrokService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey, 
        string modelCode,
        ILogger<GrokService> logger)
        : base(httpClientFactory, apiKey, modelCode, GrokBaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override string ProviderName => "Grok";

    protected override void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            _logger.LogWarning("Grok API key is not configured. Requests will likely fail.");
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
        
        try
        {
            var request = CreateRequest(requestPayload);
            _logger.LogInformation("Sending request to {ProviderName} model {ModelCode} with API Key ID (if managed): {ApiKeyId}", ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");
            if (!request.Headers.Accept.Any(h => h.MediaType == "text/event-stream"))
            {
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            }
            
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break; 
            }
            _logger.LogDebug("Successfully received stream response header from {ProviderName} model {ModelCode}", ProviderName, ModelCode);
            initialRequestSuccess = true;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
             _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled before/during HTTP send for model {ModelCode}.", ProviderName, ModelCode);
             yield break; 
        }
        catch (Exception ex) 
        {
             _logger.LogError(ex, "Error during {ProviderName} API request setup or initial response handling for model {ModelCode}.", ProviderName, ModelCode);
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