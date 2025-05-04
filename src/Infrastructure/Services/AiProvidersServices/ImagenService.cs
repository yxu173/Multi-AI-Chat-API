using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Application.Services.AI;
using Google.Apis.Auth.OAuth2;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.AiProvidersServices;

public class ImagenService : BaseAiService 
{
    private readonly string _projectId;
    private readonly string _region;
    private readonly string _imageSavePath = "wwwroot/images/imagen";
    private readonly ILogger<ImagenService>? _logger;

    public ImagenService(
        IHttpClientFactory httpClientFactory,
        string projectId,
        string region,
        string modelCode,
        ILogger<ImagenService>? logger)
        : base(httpClientFactory, null,modelCode, $"https://{region}-aiplatform.googleapis.com/")
    {
        _projectId = projectId;
        _region = region;
        _logger = logger;
        Directory.CreateDirectory(_imageSavePath);
    }

    protected override void ConfigureHttpClient()
    {
        try 
        {
             var credential = GoogleCredential.GetApplicationDefault();
             if (credential.IsCreateScopedRequired)
             {
                 credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
             }
             var token = credential.UnderlyingCredential.GetAccessTokenForRequestAsync().GetAwaiter().GetResult();
             HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
             _logger?.LogInformation("Successfully configured Google Cloud authentication token.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to configure Google Cloud authentication token.");
            throw new InvalidOperationException("Failed to configure Google Cloud authentication.", ex);
        }
    }

    protected override string GetEndpointPath() 
    {
        string publisher = "google";
        return $"v1/projects/{_projectId}/locations/{_region}/publishers/{publisher}/models/{ModelCode}:predict";
    }

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger?.LogInformation("[Imagen Raw Response] Status: {StatusCode}, Body: {ResponseBody}", response.StatusCode, jsonResponse);
        yield return jsonResponse;
    }

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(requestPayload); 
        HttpResponseMessage? response = null;
        string fullJsonResponse = string.Empty;

        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, "Imagen"); 
                yield break;
            }
            
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken))
            {
                 if (cancellationToken.IsCancellationRequested) break;
                 fullJsonResponse = jsonChunk;
                 break;
            }
        }
        catch (HttpRequestException httpEx) when (response != null)
        {
             _logger?.LogError(httpEx, "HttpRequestException during Imagen API call. Status: {StatusCode}", httpEx.StatusCode);
             yield break;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             _logger?.LogInformation("Imagen request cancelled.");
             yield break;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during Imagen API request or stream reading.");
            throw;
        }
        finally
        {
            response?.Dispose();
        }

        if (cancellationToken.IsCancellationRequested || string.IsNullOrEmpty(fullJsonResponse))
        {
             _logger?.LogInformation("Imagen request cancelled before processing or received empty response.");
             yield break;
        }

        // Variable to hold the final chunk to be yielded
        AiRawStreamChunk resultChunkPayload;

        // Parse the JSON response and generate markdown links
        try
        {
             string resultMarkdown = ParseImageResponseAndGetMarkdown(fullJsonResponse);
             // Prepare the success chunk
             resultChunkPayload = new AiRawStreamChunk(resultMarkdown, IsCompletion: true); 
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse Imagen response or save image. Response: {JsonResponse}", fullJsonResponse);
            // Prepare the error chunk
             resultChunkPayload = new AiRawStreamChunk("Error processing Imagen response.", IsCompletion: true); // Indicate completion even on error
        }
        
        // Yield the result OUTSIDE the try-catch block
        yield return resultChunkPayload;
    }

    private string ParseImageResponseAndGetMarkdown(string jsonResponse)
    {
         try
        {
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;
            var markdownResult = new StringBuilder();

            if (root.TryGetProperty("predictions", out var predictions) && predictions.ValueKind == JsonValueKind.Array)
            {
                int imageCount = 0;
                foreach (var prediction in predictions.EnumerateArray())
                {
                    if (prediction.TryGetProperty("bytesBase64Encoded", out var base64Element) && base64Element.ValueKind == JsonValueKind.String &&
                        prediction.TryGetProperty("mimeType", out var mimeTypeElement) && mimeTypeElement.ValueKind == JsonValueKind.String)
                    {
                        var base64Image = base64Element.GetString();
                        var mimeType = mimeTypeElement.GetString();

                        if (!string.IsNullOrEmpty(base64Image) && !string.IsNullOrEmpty(mimeType))
                        {
                            try
                            {
                                var imageBytes = Convert.FromBase64String(base64Image);
                                var extension = mimeType.Split('/').LastOrDefault() ?? "png";
                                var fileName = $"{Guid.NewGuid()}.{extension}";
                                var localUrl = SaveImageLocally(imageBytes, fileName);
                                markdownResult.AppendLine($"![generated image]({localUrl})");
                                imageCount++;
                            }
                            catch (FormatException ex)
                            {
                                _logger?.LogError(ex, "Error decoding base64 image from Imagen.");
                                markdownResult.AppendLine("Error processing generated image data (base64 format).");
                            }
                        }
                        else
                        {
                             markdownResult.AppendLine("Received prediction but base64 or mimeType was empty.");
                        }
                    }
                    else
                    {
                         markdownResult.AppendLine("Received prediction missing 'bytesBase64Encoded' or 'mimeType'.");
                    }
                }
                 if (imageCount == 0)
                 {
                      markdownResult.AppendLine("No valid image data found in predictions.");
                      _logger?.LogWarning("Imagen response contained predictions array but no processable image data. Response: {JsonResponse}", jsonResponse);
                 }
            }
            else
            {
                markdownResult.AppendLine("Error: 'predictions' array not found or not an array in Imagen response.");
                _logger?.LogError("Imagen Unexpected Response Structure: 'predictions' key missing or invalid. Response: {JsonResponse}", jsonResponse);
            }

            var resultString = markdownResult.ToString().Trim();
            _logger?.LogInformation("[Imagen Parsed Markdown]: {ParsedResult}", resultString);
            return resultString;
        }
        catch (JsonException jsonEx)
        {
            _logger?.LogError(jsonEx, "Error parsing Imagen JSON response. Response: {JsonResponse}", jsonResponse);
            return "Error parsing image generation response.";
        }
         catch (Exception ex)
        {
             _logger?.LogError(ex, "Unexpected error processing Imagen response. Response: {JsonResponse}", jsonResponse);
             return "Unexpected error processing image generation response.";
        }
    }

    private string SaveImageLocally(byte[] imageBytes, string fileName)
    {
        var filePath = Path.Combine(_imageSavePath, fileName);
        try
        {
            File.WriteAllBytes(filePath, imageBytes);
            return $"/images/imagen/{fileName}"; 
        }
        catch (Exception ex)
        {
             _logger?.LogError(ex, "Error saving image locally to {FilePath}", filePath);
             return $"/images/error.png"; 
        }
    }
}