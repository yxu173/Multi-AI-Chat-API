using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services;

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

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of the AI service adapter.
    /// </summary>
    protected BaseAiService(
        IHttpClientFactory httpClientFactory,
        string? apiKey,
        string modelCode,
        string baseUrl)
    {
        HttpClient = httpClientFactory.CreateClient();
        HttpClient.BaseAddress = new Uri(baseUrl);
        ApiKey = apiKey;
        ModelCode = modelCode;
        ConfigureHttpClient();
    }

    #endregion

    #region Abstract Methods

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
    public abstract IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload request,
        CancellationToken cancellationToken);

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
        
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath())
        {
            Content = new StringContent(
                // Serialize the payload object provided by the Application layer
                JsonSerializer.Serialize(requestPayload.Payload, jsonOptions), 
                Encoding.UTF8, 
                "application/json")
        };
        
        return request;
    }

    #endregion

    #region Error Handling

    /// <summary>
    /// Handles API errors by reading the response content and throwing a formatted exception.
    /// </summary>
    protected async Task HandleApiErrorAsync(HttpResponseMessage response, string providerName)
    {
        string errorContent = "Could not read error content.";
        string detailedError = "Unknown error structure.";
        try
        {
            errorContent = await response.Content.ReadAsStringAsync();
            detailedError = errorContent; // Default to raw content

            // Attempt to parse JSON error structure only if it looks like JSON
            if (!string.IsNullOrWhiteSpace(errorContent) && errorContent.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var errorJson = JsonDocument.Parse(errorContent);
                    if (errorJson.RootElement.TryGetProperty("error", out var errorObj) && errorObj.ValueKind == JsonValueKind.Object)
                    {   
                        // Try to extract common fields
                        var message = errorObj.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                        var type = errorObj.TryGetProperty("type", out var typ) ? typ.GetString() : null;
                        var param = errorObj.TryGetProperty("param", out var prm) ? prm.GetString() : null;
                        var code = errorObj.TryGetProperty("code", out var cd) ? cd.GetString() : null;

                        detailedError = $"Message: {message ?? "N/A"}, Type: {type ?? "N/A"}, Param: {param ?? "N/A"}, Code: {code ?? "N/A"}";
                    }
                    else if (errorJson.RootElement.TryGetProperty("detail", out var detailProp)) // Another common pattern
                    {
                        detailedError = $"Detail: {detailProp.ToString()}";
                    }
                    // Add more specific parsing logic here if needed for different providers
                }
                catch (JsonException jsonEx)
                { 
                    // Content started like JSON but failed to parse
                    detailedError = $"Failed to parse JSON error: {jsonEx.Message}. Raw content: {errorContent}";
                }
            }
            // If not JSON or parsing failed, detailedError remains the raw content
        }
        catch (Exception readEx)
        {
            // Error reading the error response itself
            errorContent = $"Failed to read error response: {readEx.Message}";
            detailedError = errorContent;
        }

        var errorMessage = $"{providerName} API Error ({(int)response.StatusCode} {response.ReasonPhrase}): {detailedError}";
        Console.WriteLine($"Error - {errorMessage}"); // Log the detailed error
        
        // Throw a standard exception with the formatted message
        // Pass the original exception if it was a read error (captured in the outer scope if needed, otherwise null)
        throw new HttpRequestException(errorMessage, null, response.StatusCode);
    }

    #endregion
}