using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services.Plugins;

public class WebSearchPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _searchApiKey;

    public string Id => "web-search";
    public string Name => "Web Search";
    public string Description => "Searches the web for real-time information";

    public WebSearchPlugin(HttpClient httpClient, string searchApiKey)
    {
        _httpClient = httpClient;
        _searchApiKey = searchApiKey;
    }

    public bool CanHandle(string userMessage)
    {
        // Check if the message contains search-related keywords
        var searchTerms = new[] { "search", "find", "look up", "google", "information about" };
        return searchTerms.Any(term => userMessage.ToLower().Contains(term));
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var searchQuery = ExtractSearchQuery(userMessage);


            var searchResults = await PerformWebSearch(searchQuery, cancellationToken);

            return new PluginResult(
                $"Web Search Results for '{searchQuery}':\n\n{searchResults}",
                true,
                Name
            );
        }
        catch (Exception ex)
        {
            return new PluginResult(
                "Unable to perform web search at this time.",
                false,
                ex.Message
            );
        }
    }

    private string ExtractSearchQuery(string userMessage)
    {
        foreach (var term in new[] { "search", "find", "look up", "google", "information about" })
        {
            if (userMessage.ToLower().Contains(term))
            {
                var startIndex = userMessage.ToLower().IndexOf(term) + term.Length;
                var query = userMessage.Substring(startIndex).Trim();
                if (query.StartsWith("for ") || query.StartsWith("about "))
                {
                    query = query.Substring(4).Trim();
                }

                return query;
            }
        }

        return userMessage;
    }

    private async Task<string> PerformWebSearch(string query, CancellationToken cancellationToken)
    {
        // Example using Bing Search API:
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count=5"
        );

        request.Headers.Add("Ocp-Apim-Subscription-Key", _searchApiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);


        var results = JsonDocument.Parse(jsonResponse);
        var webPages = results.RootElement.GetProperty("webPages").GetProperty("value");

        var formattedResults = new StringBuilder();
        foreach (var result in webPages.EnumerateArray())
        {
            var name = result.GetProperty("name").GetString();
            var url = result.GetProperty("url").GetString();
            var snippet = result.GetProperty("snippet").GetString();

            formattedResults.AppendLine($"- **{name}**");
            formattedResults.AppendLine($"  {snippet}");
            formattedResults.AppendLine($"  URL: {url}");
            formattedResults.AppendLine();
        }

        return formattedResults.ToString();
    }
}