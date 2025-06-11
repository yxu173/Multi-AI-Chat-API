using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
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

public class GeminiService : BaseAiService, IAiFileUploader
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";
    private readonly ILogger<GeminiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.GeminiService", "1.0.0");

    public GeminiService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey,
        string modelCode,
        ILogger<GeminiService> logger,
        IResilienceService resilienceService,
        GeminiStreamChunkParser chunkParser)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, chunkParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName)
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override string ProviderName => "Gemini";

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        // Gemini API key is usually passed in the URL for REST,
        // or via gRPC metadata. If specific headers are needed for other Gemini scenarios,
        // they would be configured here. For now, assuming key in URL is primary.
    }

    public override Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Formatting Gemini tool result for ToolCallId {ToolCallId}, ToolName {ToolName}", context.ToolCallId, context.ToolName);

        var messagePayload = new
        {
            parts = new[]
            {
                new
                {
                    functionResponse = new
                    {
                        name = context.ToolName,
                        response = new
                        {
                            content = TryParseJsonElement(context.Result) ?? (object)context.Result
                        }
                    }
                }
            }
        };
        
        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        var messageDto = new MessageDto(contentJson, false, Guid.NewGuid());
        
        return Task.FromResult(messageDto);
    }
    
    private JsonElement? TryParseJsonElement(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Tool result content was not valid JSON: {Content}", jsonString);
            return null;
        }
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
        
        // Gemini streams an array of 'GenerateContentResponse' objects.
        // Each object is a complete JSON.
        await foreach (var jsonElement in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Gemini stream reading cancelled."));
                break;
            }
            // Extract the text content if needed here, or pass the whole JSON element raw text
            // For now, passing raw JSON text of the element.
            string rawJsonChunk = jsonElement.GetRawText();
            activity?.AddEvent(new ActivityEvent("Gemini stream chunk received"));
            yield return rawJsonChunk;
        }
    }

    public async Task<AiFileUploadResult?> UploadFileForAiAsync(byte[] fileBytes, string mimeType, string fileName, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(UploadFileForAiAsync));
        activity?.SetTag("ai.provider", ProviderName);
        activity?.SetTag("file.name", fileName);
        activity?.SetTag("file.mime_type", mimeType);
        activity?.SetTag("file.size_bytes", fileBytes.Length);

        var uploadUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={ApiKey}";
        activity?.SetTag("http.url_template", "https://generativelanguage.googleapis.com/upload/v1beta/files?key=API_KEY_REDACTED");
        _logger.LogInformation("Preparing to upload file to {ProviderName}: {FileName}, MIME: {MimeType}, Size: {Size} bytes, using resilience pipeline", ProviderName, fileName, mimeType, fileBytes.Length);

        HttpResponseMessage? uploadResponse = null;
        Uri? requestUriForLogging = null; 

        try
        {
            uploadResponse = await _resiliencePipeline.ExecuteAsync(
                async ct => 
                {
                    using var attemptActivity = ActivitySource.StartActivity("UploadFileAttempt");
                    var attemptRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                    attemptRequest.Headers.Add("X-Goog-Upload-Protocol", "raw");
                    // For ByteArrayContent, it's generally safe to reuse the same byte array.
                    // If issues were to arise with content being "consumed", it would need to be new byte[fileBytes.Length] and fileBytes.CopyTo(newArray,0)
                    // but ByteArrayContent itself can typically be reused if the underlying array isn't modified.
                    attemptRequest.Content = new ByteArrayContent(fileBytes); 
                    attemptRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                    
                    requestUriForLogging = attemptRequest.RequestUri;
                    attemptActivity?.SetTag("http.url", requestUriForLogging?.ToString());
                    attemptActivity?.SetTag("http.method", HttpMethod.Post.ToString());
                    _logger.LogDebug("Attempting to upload file {FileName} to {ProviderName}: {Endpoint} via Polly pipeline", fileName, ProviderName, requestUriForLogging);
                    return await HttpClient.SendAsync(attemptRequest, ct);
                },
                cancellationToken).ConfigureAwait(false);
            
            activity?.SetTag("http.response_status_code", ((int)uploadResponse.StatusCode).ToString());
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open for file upload.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName} file upload. Request for {FileName} to {Uri} was not sent.", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "File upload timed out by Polly.");
            activity?.AddException(ex);
            _logger.LogError(ex, "{ProviderName} file upload request for {FileName} to {Uri} timed out.", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "HTTP request exception during file upload.");
            activity?.AddException(ex);
            _logger.LogError(ex, "HTTP request failed during {ProviderName} file upload for {FileName}. URI: {Uri}", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw; 
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "File upload cancelled by user.");
            activity?.AddEvent(new ActivityEvent("File upload cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} file upload cancelled for {FileName}. URI: {Uri}", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }

        if (uploadResponse == null) 
        {
            var errorMsg = $"{ProviderName} file upload response was null after resilience pipeline execution for {fileName}.";
            activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
            _logger.LogError(errorMsg);
            throw new InvalidOperationException($"Upload response was null for {fileName} with {ProviderName}.");
        }

        if (!uploadResponse.IsSuccessStatusCode)
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"File upload failed with status {uploadResponse.StatusCode}");
            _logger.LogWarning("{ProviderName} file upload failed for {FileName} with status {StatusCode}. URI: {Uri}", ProviderName, fileName, uploadResponse.StatusCode, uploadResponse.RequestMessage?.RequestUri);
            await HandleApiErrorAsync(uploadResponse, providerApiKeyId: null).ConfigureAwait(false); 
            return null;
        }

        // 2. Extract file metadata from the response
        string uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        activity?.AddEvent(new ActivityEvent("File upload response received."));
        _logger.LogDebug("Gemini file upload response for {FileName}: {ResponseBody}", fileName, uploadResponseBody);
        
        using var jsonDoc = JsonDocument.Parse(uploadResponseBody);

        if (!jsonDoc.RootElement.TryGetProperty("file", out var fileElement) ||
            !fileElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("mimeType", out var mimeTypeElement) || mimeTypeElement.ValueKind != JsonValueKind.String)
        {
            var errorMsg = "Could not parse file metadata from Gemini upload response.";
            activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
            _logger.LogError("{ErrorDetails} for {FileName}: {ResponseBody}", errorMsg, fileName, uploadResponseBody);
            throw new InvalidOperationException("Failed to parse Gemini file upload response.");
        }

        long sizeBytes = fileElement.TryGetProperty("sizeBytes", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : 0;

        var result = new AiFileUploadResult(
            ProviderFileId: nameElement.GetString()!,
            Uri: uriElement.GetString()!,
            MimeType: mimeTypeElement.GetString()!,
            SizeBytes: sizeBytes,
            OriginalFileName: fileName
        );
        activity?.SetTag("ai.file.provider_id", result.ProviderFileId);
        activity?.SetTag("ai.file.uri", result.Uri);
        _logger.LogInformation("Successfully uploaded file {FileName} to {ProviderName}. ProviderFileId: {ProviderFileId}", fileName, ProviderName, result.ProviderFileId);
        return result;
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
        _logger.LogInformation("Preparing to send request to {ProviderName} model {ModelCode} with API Key ID (if managed): {ApiKeyId} using resilience pipeline", 
            ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");

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
                    _logger.LogDebug("Attempting to send request to {ProviderName} endpoint: {Endpoint} via Polly pipeline", ProviderName, requestUriForLogging);
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
            _logger.LogDebug("Successfully received stream response header from {ProviderName} model {ModelCode}", ProviderName, ModelCode);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName} stream. Request to {Uri} was not sent.", ProviderName, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} stream timed out. URI: {Uri}", ProviderName, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Stream request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            yield break; 
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during API resilience execution.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during {ProviderName} API resilience execution for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }

        if (response == null)
        {
            yield break;
        }

        var successfulRequestUri = response.RequestMessage?.RequestUri;
        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing cancelled by token.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
                    break;
                }
                
                if (string.IsNullOrWhiteSpace(jsonChunk)) continue;

                var parsedChunk = ChunkParser.ParseChunk(jsonChunk);
                yield return parsedChunk;
                
                if (parsedChunk.FinishReason is not null)
                {
                    activity?.AddEvent(new ActivityEvent("Stream processing completed due to finish_reason."));
                    break;
                }
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("Stream completed.", tags: new ActivityTagsCollection { { "http.url", successfulRequestUri?.ToString() } }));
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}