using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Google.Apis.Auth.OAuth2;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;

namespace Infrastructure.Services.AiProvidersServices;

public class ImagenService : BaseAiService
{
    private const string ImagenBaseUrl = "https://us-central1-aiplatform.googleapis.com/";
    private readonly string _projectId;
    private readonly string _region;
    private readonly string _imageSavePath = "wwwroot/images/imagen";
    private readonly ILogger<ImagenService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.ImagenService", "1.0.0");

    public ImagenService(
        HttpClient httpClient,
        string projectId,
        string region,
        string modelCode,
        ILogger<ImagenService> logger,
        IResilienceService resilienceService)
        : base(httpClient, null, modelCode, ImagenBaseUrl, null)
    {
        _projectId = projectId;
        _region = region;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService.CreateAiServiceProviderPipeline(ProviderName);
        Directory.CreateDirectory(_imageSavePath);
    }

    protected override string ProviderName => "Imagen";

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        try
        {
             var credential = GoogleCredential.GetApplicationDefault();
             if (credential.IsCreateScopedRequired)
             {
                 credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
             }
             var token = credential.UnderlyingCredential.GetAccessTokenForRequestAsync().GetAwaiter().GetResult();
             HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
             activity?.SetTag("auth.method", "Bearer");
             _logger.LogInformation("Successfully configured Google Cloud authentication token for Imagen.");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Failed to configure Google Cloud authentication token.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Failed to configure Google Cloud authentication token for Imagen.");
            throw new InvalidOperationException("Failed to configure Google Cloud authentication for Imagen.", ex);
        }
    }

    protected override string GetEndpointPath()
    {
        const string publisher = "google";
        return $"v1/projects/{_projectId}/locations/{_region}/publishers/{publisher}/models/{ModelCode}:predict";
    }

    public override async IAsyncEnumerable<ParsedChunkInfo> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        using var activity = ActivitySource.StartActivity(nameof(StreamResponseAsync));
        activity?.SetTag("ai.provider", ProviderName);
        activity?.SetTag("ai.model", ModelCode);
        activity?.SetTag("ai.request_type", "image_generation");
        activity?.SetTag("ai.provider_api_key_id", providerApiKeyId?.ToString());

        HttpResponseMessage? response = null;
        string fullJsonResponse = string.Empty;
        ParsedChunkInfo? resultChunk = null;
        Uri? requestUriForLogging = null;

        try
        {
            response = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                using var attemptActivity = ActivitySource.StartActivity("SendHttpRequestAttempt");
                var attemptRequest = CreateRequest(requestPayload);
                requestUriForLogging = attemptRequest.RequestUri;
                attemptActivity?.SetTag("http.url", requestUriForLogging?.ToString());
                attemptActivity?.SetTag("http.method", attemptRequest.Method.ToString());
                _logger.LogInformation("Sending image generation request to {ProviderName} model {ModelCode} at {Uri}.",
                    ProviderName, ModelCode, requestUriForLogging?.ToString());
                return await HttpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("http.response_status_code", ((int)response.StatusCode).ToString());

            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"API call failed with status {response.StatusCode}");
                await HandleApiErrorAsync(response, providerApiKeyId).ConfigureAwait(false);
                yield break;
            }

            _logger.LogDebug("Successfully received response from {ProviderName} model {ModelCode}", ProviderName, ModelCode);

            fullJsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || string.IsNullOrEmpty(fullJsonResponse))
            {
                 if (!cancellationToken.IsCancellationRequested) activity?.SetStatus(ActivityStatusCode.Error, "Empty response from API.");
                 else activity?.AddEvent(new ActivityEvent("Operation cancelled before processing response body."));
            }
            else
            {
                using var parsingActivity = ActivitySource.StartActivity("ParseImageResponse");
                try
                {
                    string resultMarkdown = ParseImageResponseAndGetMarkdown(fullJsonResponse, parsingActivity);
                    resultChunk = new ParsedChunkInfo(resultMarkdown) { FinishReason = "stop" };
                    parsingActivity?.SetTag("parsing.success", true);
                    activity?.AddEvent(new ActivityEvent("Image response parsed successfully."));
                }
                catch (Exception ex)
                {
                    parsingActivity?.SetStatus(ActivityStatusCode.Error, "Failed to parse image response");
                    parsingActivity?.AddException(ex);
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to parse image response");
                    activity?.AddException(ex);
                    _logger.LogError(ex, "Error parsing Imagen image response after successful API call.");
                    resultChunk = new ParsedChunkInfo("Error: Could not process the image data from the provider.") { FinishReason = "error" };
                }
            }
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString());
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString());
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Image generation request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} image generation request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString());
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during image generation.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Unhandled exception during {ProviderName} image generation for model {ModelCode}. URI: {Uri}",
                             ProviderName, ModelCode, requestUriForLogging?.ToString());
            throw;
        }
        finally
        {
            response?.Dispose();
        }

        if (resultChunk != null)
        {
            yield return resultChunk;
        }
        else if (!cancellationToken.IsCancellationRequested)
        {
            yield return new ParsedChunkInfo("Error: Failed to generate image or process response.") { FinishReason = "error" };
        }
    }

    private string ParseImageResponseAndGetMarkdown(string jsonResponse, Activity? parentActivity)
    {
        using var activity = ActivitySource.StartActivity(nameof(ParseImageResponseAndGetMarkdown), ActivityKind.Internal, parentActivity?.Context ?? default);
        activity?.SetTag("response.json_length", jsonResponse.Length);
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
                                var localUrl = SaveImageLocally(imageBytes, fileName, activity);
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

    private string SaveImageLocally(byte[] imageBytes, string fileName, Activity? parentActivity)
    {
        using var activity = ActivitySource.StartActivity("SaveBase64ImageLocally", ActivityKind.Internal, parentActivity?.Context ?? default);
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