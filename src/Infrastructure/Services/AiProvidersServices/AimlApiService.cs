using System.Net.Http.Headers;
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

namespace Infrastructure.Services.AiProvidersServices;

public class AimlApiService : BaseAiService
{
    private const string BaseUrl = "https://api.aimlapi.com/v1/";
    private readonly string _imageSavePath = Path.Combine("wwwroot", "images", "aiml");
    private readonly ILogger<AimlApiService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.AiProvidersServices.AimlApiService", "1.0.0");

    public AimlApiService(
        IHttpClientFactory httpClientFactory,
        string? apiKey,
        string modelCode,
        ILogger<AimlApiService> logger,
        IResilienceService resilienceService)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, null)
    {
        Directory.CreateDirectory(_imageSavePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resilienceService?.CreateAiServiceProviderPipeline(ProviderName)
                            ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    protected override string ProviderName => "AimlApi";

    protected override void ConfigureHttpClient()
    {
        using var activity = ActivitySource.StartActivity(nameof(ConfigureHttpClient));
        if (!string.IsNullOrEmpty(ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            activity?.SetTag("auth.method", "Bearer");
        }
        else
        {
            activity?.AddEvent(new ActivityEvent("API key not configured."));
            _logger.LogWarning("AimlApi API key is not configured. Image generation requests will likely fail.");
        }
    }

    protected override string GetEndpointPath() => "images/generations";

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
        bool operationSuccessful = false;
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
                    operationSuccessful = true;
                    parsingActivity?.SetTag("parsing.success", true);
                    activity?.AddEvent(new ActivityEvent("Image response parsed successfully."));
                }
                catch (Exception ex)
                {
                    parsingActivity?.SetStatus(ActivityStatusCode.Error, "Failed to parse image response");
                    parsingActivity?.AddException(ex);
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to parse image response");
                    activity?.AddException(ex);
                    _logger.LogError(ex, "Error parsing AimlApi image response after successful API call.");
                    resultChunk = new ParsedChunkInfo("Error: Could not process the image data from the provider.") { FinishReason = "error" };
                }
            }
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
            activity?.AddException(ex);
            _logger.LogError(ex, "Circuit breaker is open for {ProviderName}. Request to {Uri} was not sent.", ProviderName, requestUriForLogging?.ToString() ?? (BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Request timed out by Polly");
            activity?.AddException(ex);
            _logger.LogError(ex, "Request to {ProviderName} timed out. URI: {Uri}", ProviderName, requestUriForLogging?.ToString() ?? (BaseUrl + GetEndpointPath()));
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Operation cancelled by user");
            activity?.AddEvent(new ActivityEvent("Image generation request operation was cancelled by user."));
            _logger.LogInformation(ex, "{ProviderName} image generation request operation was cancelled for model {ModelCode}. URI: {Uri}", ProviderName, ModelCode, requestUriForLogging?.ToString() ?? (BaseUrl + GetEndpointPath()));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unhandled exception during image generation.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Unhandled exception during {ProviderName} image generation for model {ModelCode}. API Key ID (if managed): {ApiKeyId}, URI: {Uri}",
                             ProviderName, ModelCode, providerApiKeyId?.ToString() ?? "Not Managed/Default", requestUriForLogging?.ToString() ?? (BaseUrl + GetEndpointPath()));
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
        else if (!operationSuccessful && !cancellationToken.IsCancellationRequested)
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

            if (root.TryGetProperty("images", out var imagesArray) && imagesArray.ValueKind == JsonValueKind.Array)
            {
                activity?.SetTag("images.count", imagesArray.GetArrayLength());
                foreach (var imageObject in imagesArray.EnumerateArray())
                {
                    string? url = null;
                    string? base64Data = null;
                    string contentType = "image/png";

                    if (imageObject.TryGetProperty("content_type", out var contentTypeElement) && contentTypeElement.ValueKind == JsonValueKind.String)
                    {
                        contentType = contentTypeElement.GetString() ?? "image/png";
                    }
                    activity?.SetTag("image.declared_content_type", contentType);

                    if (imageObject.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                    {
                        url = urlElement.GetString();
                        activity?.AddEvent(new ActivityEvent("Found image URL.", tags: new ActivityTagsCollection{ {"image.source", "url"} }));
                    }
                    else if (imageObject.TryGetProperty("b64_json", out var base64Element) && base64Element.ValueKind == JsonValueKind.String)
                    {
                        base64Data = base64Element.GetString();
                        activity?.AddEvent(new ActivityEvent("Found image b64_json data.", tags: new ActivityTagsCollection{ {"image.source", "base64"} }));
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        markdownResult.AppendLine($"![generated image]({url})");
                    }
                    else if (!string.IsNullOrEmpty(base64Data))
                    {
                        using var saveActivity = ActivitySource.StartActivity("SaveBase64ImageLocally");
                        try
                        {
                            var imageBytes = Convert.FromBase64String(base64Data);
                            var extension = contentType.Split('/').LastOrDefault()?.TrimStart('.') ?? "png";
                            var fileName = $"{Guid.NewGuid()}.{extension}";
                            saveActivity?.SetTag("file.name", fileName);
                            var localUrl = SaveImageLocally(imageBytes, fileName, saveActivity);
                            markdownResult.AppendLine($"![generated image]({localUrl})");
                            saveActivity?.SetTag("file.saved_url", localUrl);
                        }
                        catch (FormatException ex)
                        {
                            saveActivity?.SetStatus(ActivityStatusCode.Error, "Base64 format error.");
                            saveActivity?.AddException(ex);
                            _logger.LogError(ex, "Error decoding base64 image from AimlApi. Base64 snippet: {Snippet}", base64Data.Substring(0, Math.Min(base64Data.Length, 50)));
                            markdownResult.AppendLine("Error processing generated image data (base64 format error).");
                        }
                    }
                    else
                    {
                        activity?.AddEvent(new ActivityEvent("Image data found but no URL or base64 content."));
                        markdownResult.AppendLine("Received image data but couldn't find URL or base64 content.");
                    }
                }
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, "'images' array not found or not an array.");
                _logger.LogWarning("AimlApi Unexpected Response Structure. Full response length: {Length}. Body: {Body}", jsonResponse.Length, jsonResponse.Length < 2000 ? jsonResponse : jsonResponse.Substring(0,2000) + "... (truncated)");
                markdownResult.AppendLine("Error: 'images' array not found or not an array in AimlApi response.");
            }

            var resultString = markdownResult.ToString().Trim();
            activity?.SetTag("parsed_markdown_length", resultString.Length);
            return resultString;
        }
        catch (JsonException jsonEx)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "JSON parsing error.");
            activity?.AddException(jsonEx);
            _logger.LogError(jsonEx, "Error parsing AimlApi JSON response. Response snippet: {Snippet}", jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
            return "Error parsing image generation response.";
        }
         catch (Exception ex)
        {
             activity?.SetStatus(ActivityStatusCode.Error, "Unexpected error processing response.");
             activity?.AddException(ex);
             _logger.LogError(ex, "Unexpected error processing AimlApi response. Response snippet: {Snippet}", jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500)));
             return "Unexpected error processing image generation response.";
        }
    }

    private string SaveImageLocally(byte[] imageBytes, string fileName, Activity? parentActivity)
    {
        using var activity = ActivitySource.StartActivity(nameof(SaveImageLocally), ActivityKind.Internal, parentActivity?.Context ?? default);
        var filePath = Path.Combine(_imageSavePath, fileName);
        activity?.SetTag("file.path", filePath);
        try
        { 
            File.WriteAllBytes(filePath, imageBytes);
            activity?.SetTag("file.size_bytes", imageBytes.Length);
            activity?.AddEvent(new ActivityEvent("Image saved to disk."));
            _logger.LogInformation("Saved generated image locally: {FilePath}", filePath);
            return $"/images/aiml/{fileName}";
        }
        catch (Exception ex)
        { 
             activity?.SetStatus(ActivityStatusCode.Error, "Error saving image to disk.");
             activity?.AddException(ex);
             _logger.LogError(ex, "Error saving image locally to {FilePath}", filePath);
             return "/images/error.png"; 
        }
    }
} 