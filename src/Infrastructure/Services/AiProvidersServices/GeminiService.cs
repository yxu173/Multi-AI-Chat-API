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


public class GeminiService : BaseAiService, IAiFileUploader
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public GeminiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
        
        await foreach (var jsonElement in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;
            yield return jsonElement.GetRawText();
        }
    }


    public async Task<AiFileUploadResult?> UploadFileForAiAsync(byte[] fileBytes, string mimeType, string fileName, CancellationToken cancellationToken)
    {
        // 1. Upload the file bytes
        var uploadUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?key={ApiKey}";
        var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.Add("X-Goog-Upload-Protocol", "raw");
        uploadRequest.Content = new ByteArrayContent(fileBytes);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        uploadRequest.Content.Headers.ContentLength = fileBytes.Length;

        HttpResponseMessage uploadResponse = await HttpClient.SendAsync(uploadRequest, cancellationToken);

        if (!uploadResponse.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(uploadResponse, "Gemini File Upload");
            return null;
        }

        // 2. Extract file metadata from the response
        string uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDoc = JsonDocument.Parse(uploadResponseBody);

        if (!jsonDoc.RootElement.TryGetProperty("file", out var fileElement) ||
            !fileElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String ||
            !fileElement.TryGetProperty("mimeType", out var mimeTypeElement) || mimeTypeElement.ValueKind != JsonValueKind.String)
        {
            Console.WriteLine($"Error: Could not parse file metadata from Gemini upload response: {uploadResponseBody}");
            throw new InvalidOperationException("Failed to parse Gemini file upload response.");
        }

        // Extract size if available (optional, depends on API response)
        long sizeBytes = fileElement.TryGetProperty("sizeBytes", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : 0;

        return new AiFileUploadResult(
            ProviderFileId: nameElement.GetString()!, 
            Uri: uriElement.GetString()!,          // The URI to use in API calls
            MimeType: mimeTypeElement.GetString()!,
            SizeBytes: sizeBytes,                  // Populate size if available
            OriginalFileName: fileName             // Pass original filename
        );
    }

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        int maxRetries = 3;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = CreateRequest(requestPayload);
                response?.Dispose();
                
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                bool isRetryable = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;

                if (!isRetryable || attempt == maxRetries)
                {
                     await HandleApiErrorAsync(response, "Gemini");
                     yield break; 
                }
                
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 Console.WriteLine("Gemini stream request cancelled.");
                 response?.Dispose();
                 yield break;
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"Gemini HttpRequestException (Attempt {attempt}/{maxRetries}): {httpEx.Message}");
                if (attempt == maxRetries)
                { 
                    var statusCode = httpEx.StatusCode ?? System.Net.HttpStatusCode.RequestTimeout;
                    using var errorResponse = new HttpResponseMessage(statusCode) { Content = new StringContent(httpEx.Message) }; 
                    await HandleApiErrorAsync(errorResponse, "Gemini");
                    yield break;
                }
                 await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Critical error during Gemini request (Attempt {attempt}/{maxRetries}): {ex.Message}");
                 if (attempt == maxRetries) 
                 { 
                    response?.Dispose(); 
                    throw; 
                 }
                 await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            Console.WriteLine("Gemini request failed after all retries or encountered an issue.");
            response?.Dispose();
            yield break;
        }

        try
        {
            bool cancelled = false; // Flag to check if loop exited due to cancellation
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                 if (cancellationToken.IsCancellationRequested) 
                 {
                    cancelled = true; // Mark as cancelled
                    break;
                 }

                // Yield the chunk, completion is always false *during* the loop
                yield return new AiRawStreamChunk(jsonChunk, false);
            }
            
            // If the loop completed without being cancelled, signal completion
            if (!cancelled)
            {
                yield return new AiRawStreamChunk(string.Empty, true); // Signal end of stream
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}