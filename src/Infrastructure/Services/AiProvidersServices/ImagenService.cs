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
    private readonly ILogger<ImagenService> _logger;

    public ImagenService(
        IHttpClientFactory httpClientFactory,
        string projectId,
        string region,
        string modelCode,
        ILogger<ImagenService> logger)
        : base(httpClientFactory, null, modelCode, $"https://{region}-aiplatform.googleapis.com/")
    {
        _projectId = projectId;
        _region = region;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(_imageSavePath);
    }

    protected override string ProviderName => "Imagen";

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
             _logger.LogInformation("Successfully configured Google Cloud authentication token for Imagen.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Google Cloud authentication token for Imagen.");
            throw new InvalidOperationException("Failed to configure Google Cloud authentication for Imagen.", ex);
        }
    }

    protected override string GetEndpointPath() 
    {
        string publisher = "google";
        return $"v1/projects/{_projectId}/locations/{_region}/publishers/{publisher}/models/{ModelCode}:predict";
    }

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[Imagen Raw Response] Status: {StatusCode}, Body Length: {Length}", response.StatusCode, jsonResponse.Length);
        if (jsonResponse.Length < 1000) // Log small full responses for debugging
        {
            _logger.LogDebug("[Imagen Raw Response Body]: {ResponseBody}", jsonResponse);
        }
        yield return jsonResponse;
    }

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        HttpResponseMessage? response = null;
        string fullJsonResponse = string.Empty;
        AiRawStreamChunk? resultChunk = null;
        bool operationSuccessful = false;

        try
        {
            var request = CreateRequest(requestPayload); 
            _logger.LogInformation("Sending image generation request to {ProviderName} model {ModelCode} in project {ProjectId}, region {Region}.", 
                                 ProviderName, ModelCode, _projectId, _region);

            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            
            cancellationToken.ThrowIfCancellationRequested(); 
            
            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId: null).ConfigureAwait(false); 
                yield break;
            }
            
            _logger.LogDebug("Successfully received response from {ProviderName} model {ModelCode}", ProviderName, ModelCode);

            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                                .WithCancellation(cancellationToken)
                                .ConfigureAwait(false))
            {
                 if (cancellationToken.IsCancellationRequested) 
                 {
                    _logger.LogInformation("{ProviderName} response processing cancelled by token for model {ModelCode}.", ProviderName, ModelCode);
                    break;
                 }
                 fullJsonResponse = jsonChunk;
                 break; 
            }

            if (cancellationToken.IsCancellationRequested || string.IsNullOrEmpty(fullJsonResponse))
            {
                 _logger.LogInformation("{ProviderName} request cancelled or received empty/no response data before parsing for model {ModelCode}.", ProviderName, ModelCode);
                 // operationSuccessful remains false
            }
            else
            {
                 string resultMarkdown = ParseImageResponseAndGetMarkdown(fullJsonResponse); // This can throw
                 resultChunk = new AiRawStreamChunk(resultMarkdown, IsCompletion: true); 
                 operationSuccessful = true;
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
             _logger.LogInformation(ex, "{ProviderName} request cancelled during HTTP send or initial processing for model {ModelCode}.", ProviderName, ModelCode);
             // operationSuccessful remains false
        }
        // ProviderRateLimitException should propagate if HandleApiErrorAsync throws it.
        // HttpRequestException and other general exceptions during SendAsync or parsing:
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Error during {ProviderName} API request or processing for model {ModelCode}.", ProviderName, ModelCode);
            // operationSuccessful remains false.
            // If HandleApiErrorAsync was called and threw something other than ProviderRateLimitException,
            // or if parsing failed, we land here.
            // Depending on policy, create an error chunk or rethrow.
            // For now, rethrow to allow command-level handling.
            throw; 
        }
        finally
        { 
            response?.Dispose();
        }

        if (operationSuccessful && resultChunk != null)
        {
            yield return resultChunk;
        }
        // If not successful or no chunk, completes without items.
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
                                var extension = mimeType.Split('/').LastOrDefault()?.TrimStart('.') ?? "png";
                                var fileName = $"{Guid.NewGuid()}.{extension}";
                                var localUrl = SaveImageLocally(imageBytes, fileName);
                                markdownResult.AppendLine($"![generated image]({localUrl})");
                                imageCount++;
                            }
                            catch (FormatException ex)
                            {
                                _logger.LogError(ex, "Error decoding base64 image from Imagen. Snippet: {Snippet}", 
                                                 base64Image.Substring(0, Math.Min(base64Image.Length, 50)));
                                markdownResult.AppendLine("Error processing generated image data (base64 format).");
                            }
                        }
                        else
                        {
                             markdownResult.AppendLine("Received Imagen prediction but base64 or mimeType was empty.");
                             _logger.LogWarning("Empty base64 or mimeType in Imagen prediction. Snippet: {Snippet}", jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 200)));
                        }
                    }
                    else
                    {
                         markdownResult.AppendLine("Received Imagen prediction missing 'bytesBase64Encoded' or 'mimeType'.");
                         _logger.LogWarning("Missing base64 or mimeType in Imagen prediction. Snippet: {Snippet}", jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 200)));
                    }
                }
                 if (imageCount == 0 && predictions.EnumerateArray().Any())
                 {
                      markdownResult.AppendLine("No valid image data found in Imagen predictions despite receiving prediction objects.");
                      _logger.LogWarning("Imagen response contained predictions array but no processable image data. Response snippet: {Snippet}", 
                                       jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
                 }
                 else if (imageCount == 0)
                 {
                     markdownResult.AppendLine("No image predictions found in Imagen response.");
                     _logger.LogWarning("Imagen response did not contain any image predictions in the 'predictions' array. Response snippet: {Snippet}",
                                      jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
                 }
            }
            else
            {
                markdownResult.AppendLine("Error: 'predictions' array not found or not an array in Imagen response.");
                _logger.LogError("Imagen Unexpected Response Structure: 'predictions' key missing or invalid. Response snippet: {Snippet}", 
                                 jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
            }

            var resultString = markdownResult.ToString().Trim();
            _logger.LogInformation("[Imagen Parsed Markdown]: {ParsedResult}", resultString);
            return resultString;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing Imagen JSON response. Response: {JsonResponse}", jsonResponse);
            return "Error parsing image generation response.";
        }
         catch (Exception ex)
        {
             _logger.LogError(ex, "Unexpected error processing Imagen response. Response: {JsonResponse}", jsonResponse);
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
             _logger.LogError(ex, "Error saving image locally to {FilePath}", filePath);
             return $"/images/error.png"; 
        }
    }
}