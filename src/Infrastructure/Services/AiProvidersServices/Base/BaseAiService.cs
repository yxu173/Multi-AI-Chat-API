using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services;
using Application.Services.AI;
using Application.Exceptions;
using System.Net;
using System.Net.Http.Headers;
using Application.Services.AI.Streaming;
using Application.Services.Messaging;

namespace Infrastructure.Services.AiProvidersServices.Base;

/// <summary>
/// Base class for AI model service implementations - focusing on infrastructure communication.
/// </summary>
public abstract class BaseAiService : IAiModelService
{
    #region Fields and Constants

    /// <summary>
    /// HTTP client for API communication
    /// </summary>
    protected readonly HttpClient HttpClient;
    
    /// <summary>
    /// API key for the AI provider
    /// </summary>
    protected readonly string? ApiKey;
    
    /// <summary>
    /// Model identifier code (used for endpoint path, etc.)
    /// </summary>
    protected readonly string ModelCode;

    /// <summary>
    /// Parser for handling provider-specific stream chunk formats.
    /// </summary>
    protected readonly IStreamChunkParser? ChunkParser;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of the AI service adapter with proper HttpClient injection.
    /// </summary>
    protected BaseAiService(
        HttpClient httpClient,
        string? apiKey,
        string modelCode,
        string baseUrl,
        IStreamChunkParser? chunkParser)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        
        // Only set base address if not already set by typed client registration
        if (HttpClient.BaseAddress == null)
        {
            HttpClient.BaseAddress = new Uri(baseUrl);
        }
        
        ApiKey = apiKey;
        ModelCode = modelCode;
        ChunkParser = chunkParser;
        
        // Set default timeout if not set by typed client
        if (HttpClient.Timeout == TimeSpan.Zero || HttpClient.Timeout == Timeout.InfiniteTimeSpan)
        {
            HttpClient.Timeout = TimeSpan.FromSeconds(60);
        }
        
        ConfigureHttpClient();
    }
    
    /// <summary>
    /// Legacy constructor for backward compatibility
    /// </summary>
    [Obsolete("Use the constructor with direct HttpClient injection for better performance and reliability")]
    protected BaseAiService(
        IHttpClientFactory httpClientFactory,
        string? apiKey,
        string modelCode,
        string baseUrl,
        IStreamChunkParser? chunkParser)
    {
        HttpClient = httpClientFactory.CreateClient();
        HttpClient.BaseAddress = new Uri(baseUrl);
        ApiKey = apiKey;
        ModelCode = modelCode;
        ChunkParser = chunkParser;

        // Set default timeout
        HttpClient.Timeout = TimeSpan.FromSeconds(60);
        
        ConfigureHttpClient();
    }

    #endregion

    #region Abstract Properties and Methods

    /// <summary>
    /// Gets the name of the AI provider (e.g., "OpenAI", "Anthropic").
    /// </summary>
    protected abstract string ProviderName { get; }

    /// <summary>
    /// Configures the HTTP client with provider-specific headers and settings.
    /// </summary>
    protected abstract void ConfigureHttpClient();

    /// <summary>
    /// Gets the API endpoint path for the specific model provider and action.
    /// </summary>
    protected abstract string GetEndpointPath();

    /// <summary>
    /// Streams a response from the AI model based on a pre-formatted request payload.
    /// </summary>
    public virtual async IAsyncEnumerable<ParsedChunkInfo> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        if (ChunkParser is null)
        {
            // This base implementation requires a parser. 
            // If a service (like an image service) doesn't use a parser, 
            // it must provide its own complete override of this method.
            yield break;
        }

        var request = CreateRequest(requestPayload);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, providerApiKeyId);
            yield break; // Unreachable, but compiler needs it
        }

        await foreach (var rawJson in ReadStreamAsync(response, cancellationToken).WithCancellation(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                continue;
            }

            ParsedChunkInfo? parsedChunk = null;
            try
            {
                parsedChunk = ChunkParser.ParseChunk(rawJson);
            }
            catch (JsonException ex)
            {
                // Log the problematic JSON and continue if possible
                Console.WriteLine($"[BaseAiService] Failed to parse JSON chunk: {rawJson}. Error: {ex.Message}");
                // Depending on strictness, you might want to re-throw or just skip the chunk.
                // For now, we skip the malformed chunk.
            }

            if (parsedChunk != null)
            {
                yield return parsedChunk;
            }
        }
    }

    public virtual Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken)
    {
        return Task.FromException<MessageDto>(new NotSupportedException($"{ProviderName} does not support tool result formatting."));
    }

    #endregion

    #region HTTP and Streaming

    /// <summary>
    /// Reads streaming data from the HTTP response, yielding raw lines/chunks.
    /// Assumes Server-Sent Events (SSE) format by default (lines starting with "data: ").
    /// Providers might need to override this if their streaming format differs.
    /// </summary>
    protected virtual async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            // Basic SSE handling - check for cancellation inside loop
            if (cancellationToken.IsCancellationRequested) break;

            if (!string.IsNullOrEmpty(line) && line.StartsWith("data: "))
            { 
                var json = line["data: ".Length..];
                // Simple check for a common stream end marker
                if (json != "[DONE]") 
                {
                    yield return json;
                } 
            }
            else if (!string.IsNullOrEmpty(line))
            {
                // Optional: Handle non-standard lines if necessary, or log them
                 // Console.WriteLine($"Non-data line received: {line}");
            }
        }
    }

    /// <summary>
    /// Creates an HTTP request message from the pre-formatted payload.
    /// </summary>
    protected virtual HttpRequestMessage CreateRequest(AiRequestPayload requestPayload)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            // Using CamelCase to match common API styles (like OpenAI, Anthropic)
            // Providers needing different serialization might override this or adjust ConfigureHttpClient.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, 
            WriteIndented = false,
            // Ignore null values to keep payload clean
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull 
        };
        
        // Adjust endpoint path based on BaseAddress path component
        var endpointPath = GetEndpointPath();
        if (HttpClient.BaseAddress.AbsolutePath != "/" && endpointPath.StartsWith("/"))
        {
            endpointPath = endpointPath.TrimStart('/');
        }
        else if (!endpointPath.StartsWith("/") && HttpClient.BaseAddress.AbsolutePath == "/")
        {
            endpointPath = "/" + endpointPath;
        }
        
        var request = new HttpRequestMessage(HttpMethod.Post, endpointPath)
        {
            Content = new StringContent(
                // Serialize the payload object provided by the Application layer
                JsonSerializer.Serialize(requestPayload.Payload, jsonOptions), 
                Encoding.UTF8, 
                "application/json")
        };
        
        // For debugging purposes
        var payload = JsonSerializer.Serialize(requestPayload.Payload, jsonOptions);
        if (payload.Length < 1000) // Only log small payloads to avoid flooding logs
        {
            Console.WriteLine($"API Request to {endpointPath}: {payload}");
        }
        
        // Log the final Request URI before returning
        Console.WriteLine($"[BaseAiService] Requesting URI: {request.RequestUri}"); 
        
        return request;
    }

    #endregion

    #region Error Handling

    /// <summary>
    /// Handles API errors by reading the response content and throwing a formatted exception.
    /// </summary>
    protected async Task HandleApiErrorAsync(HttpResponseMessage response, Guid? providerApiKeyId)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests) // 429
        {
            TimeSpan? retryAfter = null;
            RetryConditionHeaderValue? retryAfterHeader = response.Headers.RetryAfter;
            if (retryAfterHeader != null)
            {
                if (retryAfterHeader.Delta.HasValue)
                {
                    retryAfter = retryAfterHeader.Delta.Value;
                }
                else if (retryAfterHeader.Date.HasValue)
                { 
                    retryAfter = retryAfterHeader.Date.Value - DateTimeOffset.UtcNow;
                }
            }
            // Default retry if not specified by header, e.g., 60 seconds
            retryAfter ??= TimeSpan.FromSeconds(60);

            string rateLimitMessage = $"{ProviderName} API reported a rate limit error (429 Too Many Requests).";
            try
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(errorContent)) 
                {
                    rateLimitMessage += $" Details: {errorContent.Substring(0, Math.Min(errorContent.Length, 500))}"; 
                }
            }
            catch {}
            
            Console.WriteLine($"Rate Limit Error - {rateLimitMessage} - RetryAfter: {retryAfter?.TotalSeconds}s");
            throw new ProviderRateLimitException(rateLimitMessage, HttpStatusCode.TooManyRequests, retryAfter, providerApiKeyId);
        }

        // Existing error handling for other status codes
        string errorContentFallback = "Could not read error content.";
        string detailedError = "Unknown error structure.";
        try
        {
            errorContentFallback = await response.Content.ReadAsStringAsync();
            detailedError = errorContentFallback; // Default to raw content

            if (!string.IsNullOrWhiteSpace(errorContentFallback) && errorContentFallback.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var errorJson = JsonDocument.Parse(errorContentFallback);
                    if (errorJson.RootElement.TryGetProperty("error", out var errorObj) && errorObj.ValueKind == JsonValueKind.Object)
                    {   
                        var message = errorObj.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                        var type = errorObj.TryGetProperty("type", out var typ) ? typ.GetString() : null;
                        detailedError = $"Message: {message ?? "N/A"}, Type: {type ?? "N/A"}";
                    }
                    else if (errorJson.RootElement.TryGetProperty("detail", out var detailProp))
                    {
                        detailedError = $"Detail: {detailProp.ToString()}";
                    }
                }
                catch (JsonException jsonEx)
                { 
                    detailedError = $"Failed to parse JSON error: {jsonEx.Message}. Raw content: {errorContentFallback}";
                }
            }
        }
        catch (Exception readEx)
        {
            detailedError = $"Failed to read error response: {readEx.Message}";
        }

        var errorMessage = $"{ProviderName} API Error ({(int)response.StatusCode} {response.ReasonPhrase}): {detailedError}";
        Console.WriteLine($"Error - {errorMessage}");
        throw new HttpRequestException(errorMessage, null, response.StatusCode);
    }

    #endregion
}