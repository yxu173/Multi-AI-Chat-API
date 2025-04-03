using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

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

    public bool CanHandle(string userMessage)
    {
        return userMessage.StartsWith("/google", StringComparison.OrdinalIgnoreCase) ||
               userMessage.StartsWith("/search", StringComparison.OrdinalIgnoreCase) ||
               userMessage.StartsWith("/web", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = userMessage.Replace("/google", "").Replace("/search", "").Trim();
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
            return new PluginResult("", false, $"Web search failed: {ex.Message}");
        }
    }

    private string FormatResults(GoogleSearchResponse? response)
    {
        if (response?.Items == null || response.Items.Count == 0)
            return "No results found";

        var result = new StringBuilder();
        result.AppendLine("**Google Search Results:**");
        for (int i = 0; i < Math.Min(5, response.Items.Count); i++)
        {
            var item = response.Items[i];
            result.AppendLine($"{i + 1}. [{item.Title ?? "No title"}]({item.Link ?? "#"})");
            result.AppendLine($"   {item.Snippet ?? "No description available"}");
            result.AppendLine();
        }

        return result.ToString();
    }
}

public class GoogleSearchResponse
{
    public List<SearchItem> Items { get; set; } = new List<SearchItem>();
}

public class SearchItem
{
    public string Title { get; set; }
    public string Link { get; set; }
    public string Snippet { get; set; }
}