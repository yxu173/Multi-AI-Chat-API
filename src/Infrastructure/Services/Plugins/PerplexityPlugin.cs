using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text.Json.Nodes; // For JsonObject
using System.Text.Json; // Re-added for JsonValue, JsonSerializer, etc.

public class PerplexityPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    // Tool name for AI
    public string Name => "perplexity_search";
    public string Description => "Advanced research assistant using the Perplexity Sonar API. Ideal for complex questions requiring detailed, sourced answers.";

    public PerplexityPlugin(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.BaseAddress = new Uri("https://api.perplexity.ai/"); // Set BaseAddress here
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    public JsonObject GetParametersSchema()
    {
        string schemaJson = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The query or prompt for Perplexity AI."
            }
          },
          "required": ["query"]
        }
        """;
        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    public async Task<PluginResult> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        // Note: Using System.Text.Json types here for argument parsing
        if (arguments == null || !arguments.TryGetPropertyValue("query", out var queryNode) || queryNode is not JsonValue queryValue || queryValue.GetValueKind() != JsonValueKind.String)
        {
            return new PluginResult("", false, "Missing or invalid 'query' argument for Perplexity Search.");
        }
        string query = queryValue.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PluginResult("", false, "'query' argument cannot be empty.");
        }

        try
        {
            var requestPayload = new
            {
                model = "sonar-medium-online", // Use online model
                messages = new[] {
                    new { role = "system", content = "Be precise and concise. Provide sources if available." },
                    new { role = "user", content = query }
                },
                stream = false // Explicitly non-streaming
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Headers = {
                    { "Authorization", $"Bearer {_apiKey}" },
                    { "Accept", "application/json" }
                },
                // Use EXPLICIT System.Text.Json for request serialization
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json") // Qualified
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Perplexity API Error: {response.StatusCode} - {errorBody}");
                return new PluginResult("", false, $"Perplexity API request failed with status {response.StatusCode}. Details: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // Use Newtonsoft.Json for deserialization to align with response classes
            var perplexityResponse = JsonConvert.DeserializeObject<PerplexityResponse>(responseBody);

            if (perplexityResponse?.Choices == null || perplexityResponse.Choices.Count == 0 || perplexityResponse.Choices[0].Message == null || string.IsNullOrEmpty(perplexityResponse.Choices[0].Message.Content))
            {
                Console.WriteLine($"Perplexity response missing content. Raw: {responseBody}");
                return new PluginResult("", false, "Perplexity AI did not return a valid response content.");
            }

            return new PluginResult(perplexityResponse.Choices[0].Message.Content.Trim(), true); // Trim result
        }
        // Fully qualify Newtonsoft.Json.JsonException
        catch (Newtonsoft.Json.JsonException jsonEx)
        {
            Console.WriteLine($"Error parsing Perplexity response: {jsonEx}");
            return new PluginResult("", false, $"Error processing Perplexity response: {jsonEx.Message}");
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"Perplexity HTTP Error: {httpEx}");
            return new PluginResult("", false, $"Network error during Perplexity request: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Perplexity Plugin Error: {ex}");
            return new PluginResult("", false, $"Perplexity request failed unexpectedly: {ex.Message}");
        }
    }
}

// Updated response classes to include potential 'role' and initialize properties
public class PerplexityResponse
{
    [JsonProperty("choices")]
    public List<Choice> Choices { get; set; } = new List<Choice>();
    // Add 'usage', 'id', 'model', etc. if needed from non-streaming response
}

public class Choice
{
    [JsonProperty("message")]
    public MessageContent Message { get; set; } = new MessageContent();
    [JsonProperty("finish_reason")]
    public string? FinishReason { get; set; } // Example of another potential field
}

public class MessageContent // Renamed from Messages
{
    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;
    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;
}