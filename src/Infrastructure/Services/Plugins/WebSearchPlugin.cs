using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

namespace Infrastructure.Services.Plugins
{
    public class WebSearchPlugin : IChatPlugin
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _cx;

        public WebSearchPlugin(HttpClient httpClient, string apiKey, string cx)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _cx = cx;
        }

        public bool CanHandle(string userMessage)
        {
            return userMessage?.StartsWith("/google") ?? false;
        }

        public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                var searchQuery = userMessage.Replace("/google", "").Trim();
                if (string.IsNullOrWhiteSpace(searchQuery))
                {
                    return new PluginResult(
                        "Please provide a search query after /google",
                        false,
                        "Empty search query"
                    );
                }

                var url =
                    $"https://www.googleapis.com/customsearch/v1?key={_apiKey}&cx={_cx}&q={Uri.EscapeDataString(searchQuery)}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var searchResponse = JsonConvert.DeserializeObject<GoogleSearchResponse>(json);

                return new PluginResult(
                    FormatResults(searchResponse),
                    true
                );
            }
            catch (Exception ex)
            {
                return new PluginResult(
                    $"Google Search Error: {ex.Message}",
                    false,
                    ex.Message
                );
            }
        }

        private string FormatResults(GoogleSearchResponse response)
        {
            if (response?.Items == null || response.Items.Count == 0)
                return "No results found";

            var result = new StringBuilder();
            result.AppendLine($"**Top {response.Items.Count} results:**");

            for (int i = 0; i < response.Items.Count; i++)
            {
                var item = response.Items[i];
                result.AppendLine($"{i + 1}. [{item.Title}]({item.Link})");
                result.AppendLine($"   {item.Snippet}");
                result.AppendLine();
            }

            return result.ToString();
        }
    }

    // Response DTOs
    public class GoogleSearchResponse
    {
        [JsonProperty("items")] public List<GoogleSearchResult> Items { get; set; }
    }

    public class GoogleSearchResult
    {
        [JsonProperty("title")] public string Title { get; set; }

        [JsonProperty("link")] public string Link { get; set; }

        [JsonProperty("snippet")] public string Snippet { get; set; }
    }
}