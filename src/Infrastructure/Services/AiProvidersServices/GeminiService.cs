using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;

namespace Infrastructure.Services.AiProvidersServices;

public class GeminiService : BaseAiService, IAiFileUploader
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";
    private readonly ILogger<GeminiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    public GeminiService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey,
        string modelCode,
        ILogger<GeminiService> logger,
        IResilienceService resilienceService)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName)
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override string ProviderName => "Gemini";

    protected override void ConfigureHttpClient()
    {
        // Gemini API key is usually passed in the URL for REST,
        // or via gRPC metadata. If specific headers are needed for other Gemini scenarios,
        // they would be configured here. For now, assuming key in URL is primary.
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
        
        // Gemini streams an array of 'GenerateContentResponse' objects.
        // Each object is a complete JSON.
        await foreach (var jsonElement in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Gemini stream reading cancelled.");
                break;
            }
            // Extract the text content if needed here, or pass the whole JSON element raw text
            // For now, passing raw JSON text of the element.
            string rawJsonChunk = jsonElement.GetRawText();
            _logger.LogTrace("Gemini stream chunk: {Chunk}", rawJsonChunk);
            yield return rawJsonChunk;
        }
    }

    public async Task<AiFileUploadResult?> UploadFileForAiAsync(byte[] fileBytes, string mimeType, string fileName, CancellationToken cancellationToken)
    {
        var uploadUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={ApiKey}";
        _logger.LogInformation("Preparing to upload file to {ProviderName}: {FileName}, MIME: {MimeType}, Size: {Size} bytes, using resilience pipeline", ProviderName, fileName, mimeType, fileBytes.Length);

        HttpResponseMessage? uploadResponse = null;
        Uri? requestUriForLogging = null; 

        try
        {
            uploadResponse = await _resiliencePipeline.ExecuteAsync(
                async ct => 
                {
                    // Create a new HttpRequestMessage for each attempt
                    var attemptRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                    attemptRequest.Headers.Add("X-Goog-Upload-Protocol", "raw");
                    // For ByteArrayContent, it's generally safe to reuse the same byte array.
                    // If issues were to arise with content being "consumed", it would need to be new byte[fileBytes.Length] and fileBytes.CopyTo(newArray,0)
                    // but ByteArrayContent itself can typically be reused if the underlying array isn't modified.
                    attemptRequest.Content = new ByteArrayContent(fileBytes); 
                    attemptRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                    
                    requestUriForLogging = attemptRequest.RequestUri;
                    _logger.LogDebug("Attempting to upload file {FileName} to {ProviderName}: {Endpoint} via Polly pipeline", fileName, ProviderName, requestUriForLogging);
                    return await HttpClient.SendAsync(attemptRequest, ct);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName} file upload. Request for {FileName} to {Uri} was not sent.", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            _logger.LogError(ex, "{ProviderName} file upload request for {FileName} to {Uri} timed out.", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed during {ProviderName} file upload for {FileName}. URI: {Uri}", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw; 
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("{ProviderName} file upload cancelled for {FileName}. URI: {Uri}", ProviderName, fileName, requestUriForLogging ?? new Uri(uploadUrl));
            throw;
        }

        if (uploadResponse == null) 
        {
            _logger.LogError("{ProviderName} file upload response was null after resilience pipeline execution for {FileName}. This indicates an unexpected issue.", ProviderName, fileName);
            throw new InvalidOperationException($"Upload response was null for {fileName} with {ProviderName}.");
        }

        if (!uploadResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("{ProviderName} file upload failed for {FileName} with status {StatusCode}. URI: {Uri}", ProviderName, fileName, uploadResponse.StatusCode, uploadResponse.RequestMessage?.RequestUri);
            await HandleApiErrorAsync(uploadResponse, providerApiKeyId: null).ConfigureAwait(false); 
            return null;
        }

        // 2. Extract file metadata from the response
        string uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Gemini file upload response for {FileName}: {ResponseBody}", fileName, uploadResponseBody);
        
        using var jsonDoc = JsonDocument.Parse(uploadResponseBody);

        if (!jsonDoc.RootElement.TryGetProperty("file", out var fileElement) ||
            !fileElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("mimeType", out var mimeTypeElement) || mimeTypeElement.ValueKind != JsonValueKind.String)
        {
            _logger.LogError("Error: Could not parse file metadata from Gemini upload response for {FileName}: {ResponseBody}", fileName, uploadResponseBody);
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
        _logger.LogInformation("Successfully uploaded file {FileName} to Gemini. ProviderFileId: {ProviderFileId}", fileName, result.ProviderFileId);
        return result;
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
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName} stream. Request to {Uri} was not sent.", ProviderName, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            _logger.LogError(ex, "Request to {ProviderName} stream timed out. URI: {Uri}", ProviderName, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "{ProviderName} stream request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
            yield break; 
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Error during {ProviderName} stream API resilience execution or initial response handling for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging ?? new Uri(BaseUrl + GetEndpointPath()));
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
            bool receivedAnyData = false;
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) 
                {
                    _logger.LogInformation("{ProviderName} stream processing cancelled by token for model {ModelCode}. URI: {SuccessfulRequestUri}", ProviderName, ModelCode, successfulRequestUri);
                    break;
                }
                receivedAnyData = true;
                yield return new AiRawStreamChunk(jsonChunk);
            }
            
            if (!receivedAnyData && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{ProviderName} stream for model {ModelCode} ended without sending any data. URI: {SuccessfulRequestUri}", ProviderName, ModelCode, successfulRequestUri);
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