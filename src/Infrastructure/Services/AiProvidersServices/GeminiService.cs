using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Infrastructure.Services.AiProvidersServices.Base;
using System.IO;
using System.Net.Http.Headers;
using Application.Services;
using Application.Services.AI;
using Microsoft.Extensions.Logging;

public class GeminiService : BaseAiService, IAiFileUploader
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(
        IHttpClientFactory httpClientFactory, 
        string? apiKey,
        string modelCode,
        ILogger<GeminiService> logger)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        // 1. Upload the file bytes
        var uploadUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={ApiKey}";
        _logger.LogInformation("Uploading file to Gemini: {FileName}, MIME: {MimeType}, Size: {Size} bytes", fileName, mimeType, fileBytes.Length);

        var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.Add("X-Goog-Upload-Protocol", "raw");
        uploadRequest.Content = new ByteArrayContent(fileBytes);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        // ContentLength is automatically set by ByteArrayContent

        HttpResponseMessage uploadResponse;
        try
        {
            uploadResponse = await HttpClient.SendAsync(uploadRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed during Gemini file upload for {FileName}.", fileName);
            throw; // Re-throw to be handled by the caller or a general error handler
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Gemini file upload cancelled for {FileName}.", fileName);
            throw;
        }

        if (!uploadResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini file upload failed for {FileName} with status {StatusCode}.", fileName, uploadResponse.StatusCode);
            // Passing null for providerApiKeyId as this is not a streaming content scenario
            // and might have different rate limits or no specific managed key.
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

        try
        {
            var request = CreateRequest(requestPayload);
            _logger.LogInformation("Sending request to Gemini model {ModelCode} with API Key ID (if managed): {ApiKeyId}", ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");

            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                // HandleApiErrorAsync is expected to throw. If it doesn't for some reason,
                // the method will complete without yielding, which is fine.
                yield break; 
            }
            _logger.LogDebug("Successfully received stream response header from Gemini model {ModelCode}", ModelCode);
            initialRequestSuccess = true;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Gemini stream request operation was cancelled before/during HTTP send for model {ModelCode}.", ModelCode);
            // response?.Dispose(); // Handled in finally block that should wrap stream processing too
            yield break; 
        }
        catch (Exception ex) // Catches errors from SendAsync or HandleApiErrorAsync if it doesn't throw specific handled exceptions
        {
            _logger.LogError(ex, "Error during Gemini API request setup or initial response handling for model {ModelCode}.", ModelCode);
            // response?.Dispose(); // Handled in finally block
            throw; // Re-throw for command-level retry
        }

        if (!initialRequestSuccess || response == null)
        {
            // Should have been handled by exceptions or yield break above, but as a safeguard.
            response?.Dispose();
            yield break;
        }

        // Process the stream: This part is now outside the initial try-catch for the request itself.
        // It's in its own try-finally to ensure response disposal.
        try
        {
            bool receivedAnyData = false;
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                 if (cancellationToken.IsCancellationRequested) // Should be caught by WithCancellation typically
                 {
                    _logger.LogInformation("Gemini stream processing cancelled by token for model {ModelCode}.", ModelCode);
                    break;
                 }
                receivedAnyData = true;
                yield return new AiRawStreamChunk(jsonChunk);
            }
            
            if (!receivedAnyData && !cancellationToken.IsCancellationRequested)
            {
                 _logger.LogWarning("Gemini stream for model {ModelCode} ended without sending any data.", ModelCode);
            }
            
            if (!cancellationToken.IsCancellationRequested)
            {
                 _logger.LogDebug("Gemini stream completed for model {ModelCode}.", ModelCode);
            }
        }
        // No catch here for the `yield return` part. Exceptions during ReadStreamAsync or processing will propagate.
        // OperationCanceledException during ReadStreamAsync should be handled by its own cancellation logic or propagate.
        finally
        {
            response.Dispose(); // response is guaranteed non-null here if initialRequestSuccess was true
        }
    }
}