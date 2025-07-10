using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Diagnostics;
using Application.Services.Messaging;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : BaseAiService
{
    private const string AnthropicBaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicApiVersion = "2023-06-01";
    private readonly ILogger<AnthropicService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.AnthropicService", "1.0.0");

    protected override string ProviderName => "Anthropic";

    public AnthropicService(
        HttpClient httpClient,
        string? apiKey, 
        string modelCode, 
        ILogger<AnthropicService> logger,
        IResilienceService resilienceService,
        IStreamChunkParser chunkParser)
        : base(httpClient, apiKey, modelCode, AnthropicBaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        
        ConfigureHttpClient();
    }

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        HttpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        }
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicApiVersion);
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting Anthropic tool result for ToolCallId {ToolCallId}, ToolName {ToolName}", context.ToolCallId, context.ToolName);

        var messagePayload = new
        {
            role = "user",
            content = new[]
            {
                new
                {
                    type = "tool_result",
                    tool_use_id = context.ToolCallId,
                    content = context.Result,
                    is_error = !context.WasSuccessful
                }
            }
        };
        
        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        var messageDto = new MessageDto(contentJson, false, Guid.NewGuid());
        
        return Task.FromResult(messageDto);
    }

    protected override string GetEndpointPath() => "messages";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? currentEvent = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Stream reading cancelled."));
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event: "))
            {
                currentEvent = line.Substring("event: ".Length).Trim();
                activity?.AddEvent(new ActivityEvent("Anthropic stream event", tags: new ActivityTagsCollection { { "anthropic.event_type", currentEvent } }));
            }
            else if (line.StartsWith("data: "))
            {
                var jsonData = line.Substring("data: ".Length).Trim();
                if (!string.IsNullOrWhiteSpace(jsonData))
                {
                    activity?.AddEvent(new ActivityEvent("Anthropic stream data received", tags: new ActivityTagsCollection { { "anthropic.event_type", currentEvent ?? "unknown" } }));
                    yield return jsonData;
                }
            }
            else
            {
                activity?.AddEvent(new ActivityEvent("Anthropic stream ignored line", tags: new ActivityTagsCollection { { "line_content_preview", line.Substring(0, Math.Min(line.Length, 50)) } }));
            }
        }
        if (cancellationToken.IsCancellationRequested)
        {
            activity?.AddEvent(new ActivityEvent("Finished reading Anthropic stream due to cancellation."));
        }
        else
        {
            activity?.AddEvent(new ActivityEvent("Finished reading Anthropic stream (end of stream)."));
        }
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
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            yield break;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (AnthropicBaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri;
        activity?.AddEvent(new ActivityEvent("Anthropic request successful, beginning to process stream.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() }, { "http.status_code", response.StatusCode.ToString()} }));
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(jsonChunk)) continue;

                var parsedChunk = ChunkParser.ParseChunk(jsonChunk);
                yield return parsedChunk;

                if (parsedChunk.FinishReason is not null)
                {
                    activity?.AddEvent(new ActivityEvent("Anthropic stream finished.", tags: new ActivityTagsCollection { { "finish_reason", parsedChunk.FinishReason } }));
                    break;
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Finished reading Anthropic stream successfully."));
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}