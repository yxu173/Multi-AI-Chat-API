using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;

public class DeepSeekService : BaseAiService
{
    private const string BaseUrl = "https://api.deepseek.com/v1/";
    private readonly ILogger<DeepSeekService> _logger;

    public DeepSeekService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey,
        string modelCode,
        ILogger<DeepSeekService> logger)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        try
        {
            var request = CreateRequest(requestPayload);
            _logger.LogInformation("Sending request to {ProviderName} model {ModelCode} with API Key ID (if managed): {ApiKeyId}", ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");

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
                            _logger.LogDebug("{ProviderName} stream indicates completion. Finish reason: {FinishReason}", ProviderName, finishReason.GetString());
                            isCompletion = true;
                         }
                    }
                }
                catch (JsonException jsonEx) 
                { 
                    _logger.LogWarning(jsonEx, "Failed to parse JSON chunk from {ProviderName}: {JsonChunk}", ProviderName, jsonChunk);
                }

                yield return new AiRawStreamChunk(jsonChunk, isCompletion);
                if (isCompletion) 
                {
                    _logger.LogDebug("{ProviderName} stream processing completed due to finish_reason for model {ModelCode}.", ProviderName, ModelCode);
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
}