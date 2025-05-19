using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : BaseAiService
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1/";
    private readonly ILogger<OpenAiService> _logger;

    protected override string ProviderName => "OpenAI";

    public OpenAiService(IHttpClientFactory httpClientFactory, string? apiKey, string modelCode, ILogger<OpenAiService> logger)
        : base(httpClientFactory, apiKey, modelCode, OpenAiBaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        var request = CreateRequest(requestPayload);
        HttpResponseMessage? response = null;
        bool initialRequestSuccess = false;

        try
        {
            _logger.LogDebug("Sending request to OpenAI endpoint: {Endpoint}", request.RequestUri);
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }
            _logger.LogDebug("Successfully received stream response header from OpenAI model {ModelCode}", ModelCode);
            initialRequestSuccess = true;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "OpenAI stream request operation was cancelled before/during HTTP send for model {ModelCode}.", ModelCode);
            yield break;
        }
        catch (Exception ex) // Catches errors from SendAsync or HandleApiErrorAsync
        {
            _logger.LogError(ex, "Error during OpenAI API request setup or initial response handling for model {ModelCode}. URI: {Uri}", ModelCode, request.RequestUri);
            throw; 
        }
        
        if (!initialRequestSuccess || response == null) 
        {
            // This case should ideally be covered by the exceptions or yield break above.
            response?.Dispose();
            yield break;
        }

        // Process the stream
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                // No need to check cancellationToken.IsCancellationRequested here if WithCancellation is effective
                // and ReadStreamAsync itself handles cancellation appropriately by breaking or throwing.
                yield return new AiRawStreamChunk(jsonChunk);
            }
            // Log completion if not cancelled
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Finished reading OpenAI stream for request to {Endpoint}", request.RequestUri);
            }
        }
        // REMOVED CATCH BLOCK HERE to prevent CS1626. Exceptions during stream processing will propagate.
        // The command level is responsible for handling these propagated exceptions.
        finally
        {
            response.Dispose(); // response is guaranteed non-null here
        }
    }
}