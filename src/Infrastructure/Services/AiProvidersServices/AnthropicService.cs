using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Application.Services.AI;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System;

public class AnthropicService : BaseAiService
{
    private const string AnthropicBaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicApiVersion = "2023-06-01";
    private readonly ILogger<AnthropicService> _logger;

    protected override string ProviderName => "Anthropic";

    public AnthropicService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey, 
        string modelCode, 
        ILogger<AnthropicService> logger)
        : base(httpClientFactory, apiKey, modelCode, AnthropicBaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        }
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicApiVersion);
        HttpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    protected override string GetEndpointPath() => "messages";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? currentEvent = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) break;

            if (string.IsNullOrWhiteSpace(line))
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event: "))
            {
                currentEvent = line.Substring("event: ".Length).Trim();
                _logger.LogTrace("Anthropic stream event type: {EventType}", currentEvent);
            }
            else if (line.StartsWith("data: "))
            {
                var jsonData = line.Substring("data: ".Length).Trim();
                if (!string.IsNullOrWhiteSpace(jsonData))
                {
                    _logger.LogTrace("Anthropic stream data for event '{EventType}': {JsonData}", currentEvent ?? "unknown", jsonData.Substring(0, Math.Min(jsonData.Length, 100)));
                    yield return jsonData;
                }
            }
            else
            {
                _logger.LogTrace("Anthropic stream ignored line: {Line}", line);
            }
        }
        _logger.LogTrace("Finished reading from Anthropic stream or cancellation requested.");
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
            _logger.LogDebug("Sending request to Anthropic endpoint: {Endpoint}, Model: {Model}", request.RequestUri, ModelCode);
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }
            _logger.LogDebug("Successfully received stream response header from Anthropic model {ModelCode}", ModelCode);
            initialRequestSuccess = true;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Anthropic stream request operation was cancelled before/during HTTP send for model {ModelCode}. URI: {Uri}", ModelCode, request.RequestUri);
            yield break;
        }
        catch (Exception ex) // Catches errors from SendAsync or HandleApiErrorAsync
        {
            _logger.LogError(ex, "Error during Anthropic API request setup or initial response handling for model {ModelCode}. URI: {Uri}", ModelCode, request.RequestUri);
            throw; 
        }
        
        if (!initialRequestSuccess || response == null) 
        {
            response?.Dispose();
            yield break;
        }

        // Process the stream
        _logger.LogDebug("Anthropic request successful (HTTP {StatusCode}), beginning to process stream.", response.StatusCode);
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                // ReadStreamAsync for Anthropic should handle cancellation internally or by WithCancellation.
                bool isCompletion = false;
                string? eventTypeWithinJson = null;
                try
                {
                    using var doc = JsonDocument.Parse(jsonChunk);
                    if (doc.RootElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                    {
                        eventTypeWithinJson = typeElement.GetString();
                        if (eventTypeWithinJson == "message_stop") 
                        {
                            isCompletion = true;
                            _logger.LogDebug("Anthropic message_stop event received.");
                        }
                        if (eventTypeWithinJson == "error")
                        {
                            _logger.LogError("Error event received in Anthropic stream: {JsonChunk}", jsonChunk);
                            // Potentially break or handle as a stream-terminating error depending on policy.
                            // For now, we yield it and let downstream decide. If it means the stream is unusable, consider breaking.
                        }
                    }
                }
                catch (JsonException jsonEx)
                { 
                    _logger.LogWarning(jsonEx, "JSON parsing error in Anthropic stream chunk. Raw chunk: {JsonChunk}", jsonChunk);
                }
                _logger.LogTrace("Yielding Anthropic chunk. Event in JSON: '{EventType}', IsCompletion: {IsCompletion}", eventTypeWithinJson ?? "N/A", isCompletion);
                yield return new AiRawStreamChunk(jsonChunk, isCompletion);
                if (isCompletion) break; // Stop processing if it's the final message_stop event.
            }
            
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Finished reading Anthropic stream for request to {Endpoint}", request.RequestUri);
            }
        }
        finally
        {
            response.Dispose(); // response is guaranteed non-null here
        }
    }
}