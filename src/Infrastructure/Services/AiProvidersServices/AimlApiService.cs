using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.AiProvidersServices;

public class AimlApiService : BaseAiService
{
    private const string BaseUrl = "https://api.aimlapi.com/v1/";
    private readonly string _imageSavePath = "wwwroot/images/aiml"; // Specific path for AIML images
    private readonly ILogger<AimlApiService> _logger;

    public AimlApiService(IHttpClientFactory httpClientFactory, string? apiKey, string modelCode, ILogger<AimlApiService> logger)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
        Directory.CreateDirectory(_imageSavePath); // Ensure the directory exists
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override string ProviderName => "AimlApi";

    protected override void ConfigureHttpClient()
    {
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }
        else
        {
            _logger.LogWarning("AimlApi API key is not configured. Image generation requests will likely fail.");
        }
    }

    protected override string GetEndpointPath() => "images/generations";

    // Override ReadStreamAsync to handle non-streaming JSON response
    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[AimlApi Raw Response] Status: {StatusCode}, Body Length: {Length}", response.StatusCode, jsonResponse.Length);
        if (jsonResponse.Length < 1000) // Log small full responses for debugging
        {
            _logger.LogDebug("[AimlApi Raw Response Body]: {ResponseBody}", jsonResponse);
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
            _logger.LogInformation("Sending image generation request to {ProviderName} model {ModelCode} with API Key ID (if managed): {ApiKeyId}", 
                                 ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");

            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false); 
                // Assuming HandleApiErrorAsync throws. If not, operationSuccessful remains false.
                yield break; // Exit if error is handled and thrown by HandleApiErrorAsync
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
                 _logger.LogInformation("{ProviderName} request cancelled or yielded no response data for model {ModelCode}.", ProviderName, ModelCode);
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
             _logger.LogInformation(ex, "{ProviderName} image generation request operation was cancelled for model {ModelCode}.", ProviderName, ModelCode);
             // operationSuccessful remains false
        }
        // Specific exceptions like ProviderRateLimitException should be caught by the command if HandleApiErrorAsync throws them.
        // Or, catch them here if specific local handling (not rethrow) is needed.
        catch (Exception ex) // Catch other exceptions from SendAsync, HandleApiErrorAsync (if it doesn't throw specific types), or ParseImageResponseAndGetMarkdown
        { 
            _logger.LogError(ex, "Unhandled exception during {ProviderName} image generation for model {ModelCode}. API Key ID (if managed): {ApiKeyId}", 
                             ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default");
            // operationSuccessful remains false. Consider creating an error chunk if appropriate for the caller.
            // For now, let command level handle by rethrowing or if no resultChunk is yielded.
            // If we want to yield an error chunk: 
            // resultChunk = new AiRawStreamChunk($\"Error generating image: {ex.Message}\", IsCompletion: true); 
            // operationSuccessful = true; // if we decide to yield this error chunk.
            throw; // Re-throw to allow command-level retry logic to handle it.
        }
        finally
        {
            response?.Dispose();
        }

        if (operationSuccessful && resultChunk != null)
        {
            yield return resultChunk;
        }
        // If !operationSuccessful or resultChunk is null, the enumerable completes without items.
    }

    private string ParseImageResponseAndGetMarkdown(string jsonResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;
            var markdownResult = new StringBuilder();

            if (root.TryGetProperty("images", out var imagesArray) && imagesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var imageObject in imagesArray.EnumerateArray())
                {
                    string? url = null;
                    string? base64Data = null;
                    string contentType = "image/png";

                    if (imageObject.TryGetProperty("content_type", out var contentTypeElement) && contentTypeElement.ValueKind == JsonValueKind.String)
                    {
                        contentType = contentTypeElement.GetString() ?? "image/png";
                    }

                    if (imageObject.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                    {
                        url = urlElement.GetString();
                    }
                    else if (imageObject.TryGetProperty("b64_json", out var base64Element) && base64Element.ValueKind == JsonValueKind.String)
                    {
                        base64Data = base64Element.GetString();
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        markdownResult.AppendLine($"![generated image]({url})");
                    }
                    else if (!string.IsNullOrEmpty(base64Data))
                    {
                        try
                        {
                            var imageBytes = Convert.FromBase64String(base64Data);
                            var extension = contentType.Split('/').LastOrDefault()?.TrimStart('.') ?? "png"; 
                            var fileName = $"{Guid.NewGuid()}.{extension}";
                            var localUrl = SaveImageLocally(imageBytes, fileName);
                            markdownResult.AppendLine($"![generated image]({localUrl})");
                        }
                        catch (FormatException ex)
                        {
                            _logger.LogError(ex, "Error decoding base64 image from AimlApi. Base64 snippet: {Snippet}", base64Data.Substring(0, Math.Min(base64Data.Length, 50)));
                            markdownResult.AppendLine("Error processing generated image data (base64 format error).");
                        }
                    }
                    else
                    {
                        markdownResult.AppendLine("Received image data but couldn't find URL or base64 content.");
                    }
                }
            }
            else
            {
                 markdownResult.AppendLine("Error: 'images' array not found or not an array in AimlApi response.");
                 _logger.LogWarning("AimlApi Unexpected Response Structure. Full response length: {Length}. Body: {Body}", jsonResponse.Length, jsonResponse.Length < 2000 ? jsonResponse : jsonResponse.Substring(0,2000) + "... (truncated)");
            }

            var resultString = markdownResult.ToString().Trim();
            _logger.LogInformation("[AimlApi Parsed Markdown]: {ParsedResult}", resultString);
            return resultString;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing AimlApi JSON response. Response snippet: {Snippet}", jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
            return "Error parsing image generation response.";
        }
         catch (Exception ex)
        {
             _logger.LogError(ex, "Unexpected error processing AimlApi response. Response snippet: {Snippet}", jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
             return "Unexpected error processing image generation response.";
        }
    }

    private string SaveImageLocally(byte[] imageBytes, string fileName)
    {
        var filePath = Path.Combine(_imageSavePath, fileName);
        try
        { 
            File.WriteAllBytes(filePath, imageBytes);
            _logger.LogInformation("Saved generated image locally: {FilePath}", filePath);
            return $"/images/aiml/{fileName}";
        }
        catch (Exception ex)
        { 
             _logger.LogError(ex, "Error saving image locally to {FilePath}", filePath);
             return "/images/error.png"; 
        }
    }
} 