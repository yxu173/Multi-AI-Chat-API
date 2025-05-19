using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

namespace Infrastructure.Services.Plugins;

public class WebSearchPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _cx;

    public string Name => "google_search";
    public string Description => "Search the web using Google for real-time information";

    public WebSearchPlugin(HttpClient httpClient, string apiKey, string cx)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _cx = cx ?? throw new ArgumentNullException(nameof(cx));
    }

    // Removed CanHandle method

    public JsonObject GetParametersSchema()
    {
        // Define the schema the AI needs to provide arguments
        string schemaJson = """
                            {
                              "type": "object",
                              "properties": {
                                "query": {
                                  "type": "string",
                                  "description": "The search query to execute."
                                }
                              },
                              "required": ["query"]
                            }
                            """;
        // Parse and return as JsonObject
        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    // Modified ExecuteAsync to accept JsonObject arguments
    public async Task<PluginResult> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        // Extract the 'query' argument from the JsonObject provided by the AI
        if (arguments == null || !arguments.TryGetPropertyValue("query", out var queryNode) || queryNode is not JsonValue queryValue || queryValue.GetValueKind() != JsonValueKind.String)
        {
            return new PluginResult("", false, "Missing or invalid 'query' argument for Google Search.");
        }

        string query = queryValue.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PluginResult("", false, "'query' argument cannot be empty.");
        }

        try
        {
            // The rest of the execution logic remains largely the same
            var url =
                $"https://www.googleapis.com/customsearch/v1?key={_apiKey}&cx={_cx}&q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResponse = JsonConvert.DeserializeObject<GoogleSearchResponse>(json);
            var result = FormatResults(searchResponse);
            return new PluginResult(result, true);
        }
        catch (Exception ex)
        {
            // Log the exception details if possible
            Console.WriteLine($"Google Search Plugin Error: {ex}");
            return new PluginResult("", false, $"Web search failed: {ex.Message}");
        }
    }

    private string FormatResults(GoogleSearchResponse? response)
    {
        if (response?.Items == null || response.Items.Count == 0)
            return "No results found";

        var result = new StringBuilder();
        // Keep formatting concise for AI consumption
        result.AppendLine("Google Search Results:");
        for (int i = 0; i < Math.Min(5, response.Items.Count); i++) // Limit results
        {
            var item = response.Items[i];
            result.AppendLine($"- **{item.Title}**: {item.Snippet} ([Link]({item.Link ?? "#"}))");
        }

        return result.ToString();
    }
}

// GoogleSearchResponse and SearchItem remain the same
public class GoogleSearchResponse
{
    public List<SearchItem> Items { get; set; } = new List<SearchItem>();
}

public class SearchItem
{
    public string Title { get; set; } = string.Empty; // Initialize to avoid nulls
    public string Link { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}