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
    private readonly ILogger<AimlApiService>? _logger;

    public AimlApiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode, ILogger<AimlApiService>? logger)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
        Directory.CreateDirectory(_imageSavePath); // Ensure the directory exists
        _logger = logger;
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
    }

    protected override string GetEndpointPath() => "images/generations";

    // Override ReadStreamAsync to handle non-streaming JSON response
    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        // Log the raw response
        _logger?.LogInformation("[AIMLAPI Raw Response] Status: {StatusCode}, Body: {ResponseBody}", response.StatusCode, jsonResponse);
        // Since it's not a stream of events, yield the entire JSON once.
        yield return jsonResponse;
    }

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(requestPayload);
        HttpResponseMessage? response = null;

        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException httpEx)
        {
            await HandleApiErrorAsync(response ?? new HttpResponseMessage(httpEx.StatusCode ?? System.Net.HttpStatusCode.InternalServerError), "AIMLAPI");
            yield break;
        }
        catch (Exception ex)
        { 
            Console.WriteLine($"Error sending request to AIMLAPI: {ex.Message}");
            // Potentially rethrow or handle more gracefully depending on policy
            throw;
        }

        try
        {
            // Read the single JSON response
            string fullJsonResponse = string.Empty;
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken))
            {
                 if (cancellationToken.IsCancellationRequested) break;
                 fullJsonResponse = jsonChunk; // Capture the full response
                 break; // Expecting only one item for non-streaming response
            }
            
            if (cancellationToken.IsCancellationRequested || string.IsNullOrEmpty(fullJsonResponse))
            {
                 yield break;
            }

            // Parse the JSON and generate markdown
            string resultMarkdown = ParseImageResponseAndGetMarkdown(fullJsonResponse);
            
            // Yield a single chunk with the result and mark as complete
            yield return new AiRawStreamChunk(resultMarkdown, IsCompletion: true);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private string ParseImageResponseAndGetMarkdown(string jsonResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;
            var markdownResult = new StringBuilder();

            // Look for the "images" array based on the provided sample response
            if (root.TryGetProperty("images", out var imagesArray) && imagesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var imageObject in imagesArray.EnumerateArray())
                {
                    string? url = null;
                    string? base64Data = null;
                    string contentType = "image/png"; // Default

                    // Extract content_type to determine the file extension if needed
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
                        // Support base64 if URL is not provided (though sample uses URL)
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
                            // Derive extension from content_type
                            var extension = contentType.Split('/').LastOrDefault() ?? "png"; 
                            var fileName = $"{Guid.NewGuid()}.{extension}";
                            var localUrl = SaveImageLocally(imageBytes, fileName);
                            markdownResult.AppendLine($"![generated image]({localUrl})");
                        }
                        catch (FormatException ex)
                        {
                            Console.WriteLine($"Error decoding base64 image from AIMLAPI: {ex.Message}");
                            markdownResult.AppendLine("Error processing generated image data.");
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
                 // Log the actual response if the expected structure isn't found
                 markdownResult.AppendLine("Error: 'images' array not found or not an array in AIMLAPI response.");
                 Console.WriteLine($"AIMLAPI Unexpected Response Structure: {jsonResponse}");
            }

            var resultString = markdownResult.ToString().Trim();
            // Log the parsed result
            _logger?.LogInformation("[AIMLAPI Parsed Markdown]: {ParsedResult}", resultString);
            return resultString;
        }
        catch (JsonException jsonEx)
        {
            Console.WriteLine($"Error parsing AIMLAPI JSON response: {jsonEx.Message}. Response: {jsonResponse}");
            return "Error parsing image generation response.";
        }
         catch (Exception ex)
        {
             Console.WriteLine($"Unexpected error processing AIMLAPI response: {ex.Message}. Response: {jsonResponse}");
             return "Unexpected error processing image generation response.";
        }
    }

    private string SaveImageLocally(byte[] imageBytes, string fileName)
    {
        var filePath = Path.Combine(_imageSavePath, fileName);
        try
        { 
            File.WriteAllBytes(filePath, imageBytes);
            // Return a relative URL path for web access
            return $"/images/aiml/{fileName}";
        }
        catch (Exception ex)
        { 
             Console.WriteLine($"Error saving image locally to {filePath}: {ex.Message}");
             return $"/images/error.png"; // Placeholder or error indicator
        }
    }
} 