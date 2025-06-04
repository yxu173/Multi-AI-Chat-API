using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Logging;
using Polly;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Infrastructure.Services.AiProvidersServices;

public class AimlApiService : BaseAiService
{
    private const string BaseUrl = "https://api.bfl.ai/";
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
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
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
            HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            HttpClient.DefaultRequestHeaders.Add("x-key", ApiKey);
            activity?.SetTag("auth.method", "X-Key");
        }
        else
        {
            activity?.AddEvent(new ActivityEvent("API key not configured."));
            _logger.LogWarning("BFL API key is not configured. Image generation requests will likely fail.");
        }
    }

    protected override string GetEndpointPath() => $"v1/{ModelCode}";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadStreamAsync));
        activity?.SetTag("http.response_status_code", response.StatusCode.ToString());
        
        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        
        activity?.SetTag("response.content_length", jsonResponse.Length);
        activity?.AddEvent(new ActivityEvent("Full response content read."));
        yield return jsonResponse;
    }

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
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
        AiRawStreamChunk? resultChunk = null;
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

            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                                .WithCancellation(cancellationToken)
                                .ConfigureAwait(false))
            {
                 if (cancellationToken.IsCancellationRequested)
                 {
                    activity?.AddEvent(new ActivityEvent("Response processing cancelled by token.", tags: new ActivityTagsCollection { { "http.url", response.RequestMessage?.RequestUri?.ToString() } }));
                    break;
                 }
                 fullJsonResponse = jsonChunk;
                 break;
            }

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
                    // Parse the initial response to get the request ID
                    using var document = JsonDocument.Parse(fullJsonResponse);
                    var root = document.RootElement;
                    
                    if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                    {
                        throw new JsonException("Request ID not found in response");
                    }
                    
                    string requestId = idElement.GetString()!;
                    activity?.SetTag("request.id", requestId);
                    
                    // Poll for the result
                    string resultMarkdown = await PollForImageResultAsync(requestId, parsingActivity, cancellationToken);
                    resultChunk = new AiRawStreamChunk(resultMarkdown, IsCompletion: true);
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
                    _logger.LogError(ex, "Error parsing BFL API image response after successful API call.");
                    resultChunk = new AiRawStreamChunk("Error: Could not process the image data from the provider.", IsCompletion: true);
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
            yield return new AiRawStreamChunk("Error: Failed to generate image or process response.", IsCompletion: true);
        }
    }

    private async Task<string> PollForImageResultAsync(string requestId, Activity? parentActivity, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(PollForImageResultAsync), ActivityKind.Internal, parentActivity?.Context ?? default);
        activity?.SetTag("request.id", requestId);

        try
        {
            int maxAttempts = 30;  // Adjust as needed
            int attempt = 0;
            int delayMs = 1000;  // Start with 1 second delay

            while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                attempt++;
                activity?.SetTag("polling_attempt", attempt);
                
                // Wait before polling (except for first attempt)
                if (attempt > 1)
                {
                    await Task.Delay(delayMs, cancellationToken);
                    // Increase delay for next attempt, capped at 5 seconds
                    delayMs = Math.Min(delayMs * 2, 5000);
                }
                
                using var pollingRequest = new HttpRequestMessage(HttpMethod.Get, $"v1/get_result?id={requestId}");
                if (!string.IsNullOrEmpty(ApiKey))
                {
                    pollingRequest.Headers.Add("x-key", ApiKey);
                }
                
                var pollingResponse = await HttpClient.SendAsync(pollingRequest, cancellationToken);
                
                if (!pollingResponse.IsSuccessStatusCode)
                {
                    activity?.SetTag($"polling_attempt_{attempt}_status", (int)pollingResponse.StatusCode);
                    continue;
                }
                
                var pollingJsonResponse = await pollingResponse.Content.ReadAsStringAsync(cancellationToken);
                using var pollingDocument = JsonDocument.Parse(pollingJsonResponse);
                var pollingRoot = pollingDocument.RootElement;
                
                // Check status
                if (pollingRoot.TryGetProperty("status", out var statusElement) && 
                    statusElement.ValueKind == JsonValueKind.String)
                {
                    string status = statusElement.GetString()!;
                    activity?.SetTag($"polling_attempt_{attempt}_status", status);
                    
                    if (status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                    {
                        // Success! We have the image data
                        if (pollingRoot.TryGetProperty("result", out var resultElement) &&
                            resultElement.TryGetProperty("sample", out var sampleElement))
                        {
                            var imageUrls = new List<string>();
                            
                            // Handle both single image and array of images
                            if (sampleElement.ValueKind == JsonValueKind.String)
                            {
                                imageUrls.Add(sampleElement.GetString()!);
                            }
                            else if (sampleElement.ValueKind == JsonValueKind.Array)
                            {
                                // Limit to maximum 4 images
                                foreach (var imageUrl in sampleElement.EnumerateArray().Take(4))
                                {
                                    if (imageUrl.ValueKind == JsonValueKind.String)
                                    {
                                        imageUrls.Add(imageUrl.GetString()!);
                                    }
                                }
                            }

                            if (imageUrls.Count == 0)
                            {
                                throw new JsonException("No image URLs found in result");
                            }

                            var markdownImages = new List<string>();
                            foreach (var imageUrl in imageUrls)
                            {
                                // Download and save the image locally
                                using var imageResponse = await HttpClient.GetAsync(imageUrl, cancellationToken);
                                if (imageResponse.IsSuccessStatusCode)
                                {
                                    var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
                                    var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
                                    var extension = contentType.Split('/').LastOrDefault()?.TrimStart('.') ?? "png";
                                    var fileName = $"{Guid.NewGuid()}.{extension}";
                                    
                                    var localUrl = SaveImageLocally(imageBytes, fileName, activity);
                                    markdownImages.Add($"![generated image]({localUrl})");
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to download image from URL: {Url}", imageUrl);
                                    markdownImages.Add($"![generated image]({imageUrl})");
                                }
                            }
                            
                            // Join all markdown images with newlines
                            var markdownImage = string.Join("\n\n", markdownImages);
                            
                            // Wrap the markdown in a JSON structure
                            return JsonSerializer.Serialize(new { text = markdownImage });
                        }
                        else
                        {
                            throw new JsonException("Image URL not found in result");
                        }
                    }
                    else if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase) || 
                             status.Equals("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        // Handle error
                        string errorMessage = "Image generation failed";
                        if (pollingRoot.TryGetProperty("error", out var errorElement))
                        {
                            errorMessage = errorElement.GetString() ?? errorMessage;
                        }
                        activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
                        return JsonSerializer.Serialize(new { error = errorMessage });
                    }
                    // For "Processing" or "Queued" statuses, continue polling
                }
            }
            
            // If we reach here, we've timed out
            activity?.SetStatus(ActivityStatusCode.Error, "Polling timed out");
            return JsonSerializer.Serialize(new { error = "Image generation timed out" });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error polling for image result");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error polling for image result from BFL API");
            return JsonSerializer.Serialize(new { error = "Failed to retrieve generated image" });
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

    private HttpRequestMessage CreateRequest(AiRequestPayload payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath());
        var content = new StringContent(
            JsonSerializer.Serialize(payload.Payload),
            Encoding.UTF8,
            "application/json"
        );
        request.Content = content;
        return request;
    }
} 