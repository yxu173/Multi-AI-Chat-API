using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

namespace Infrastructure.Services.Plugins;

public class WikipediaPlugin : IChatPlugin<string>
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://en.wikipedia.org/api/rest_v1";
    private readonly string _wikiApiUrl = "https://en.wikipedia.org/w/api.php";

    public string Name => "wikipedia_search";
    public string Description => "Search Wikipedia for information on a specific topic or term";

    public WikipediaPlugin(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public JsonObject GetParametersSchema()
    {
        string schemaJson = """
                            {
                              "type": "object",
                              "properties": {
                                "query": {
                                  "type": "string",
                                  "description": "The search term to look up on Wikipedia."
                                },
                                "limit": {
                                  "type": "integer",
                                  "description": "Maximum number of results to return (1-10).",
                                  "default": 3
                                }
                              },
                              "required": ["query"]
                            }
                            """;
        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    public async Task<PluginResult<string>> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        if (arguments == null || !arguments.TryGetPropertyValue("query", out var queryNode) ||
            queryNode is not JsonValue queryValue || queryValue.GetValueKind() != JsonValueKind.String)
        {
            return new PluginResult<string>("Missing or invalid 'query' argument for Wikipedia search.", false);
        }

        string query = queryValue.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PluginResult<string>("'query' argument cannot be empty.", false);
        }

        int limit = 3;
        if (arguments.TryGetPropertyValue("limit", out var limitNode) && limitNode is JsonValue limitValue &&
            limitValue.GetValueKind() == JsonValueKind.Number)
        {
            limit = Math.Min(10, Math.Max(1, limitValue.GetValue<int>())); // Clamp between 1 and 10
        }

        try
        {
            var searchUrl =
                $"{_wikiApiUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json&srlimit={limit}";
            var searchResponse = await _httpClient.GetAsync(searchUrl, cancellationToken);
            searchResponse.EnsureSuccessStatusCode();
            var searchJson = await searchResponse.Content.ReadAsStringAsync(cancellationToken);

            var searchData = JsonConvert.DeserializeObject<MediaWikiSearchResponse>(searchJson);

            if (searchData?.Query?.Search == null || !searchData.Query.Search.Any())
            {
                return new PluginResult<string>("No Wikipedia results found for the query.", true);
            }

            var searchResults = new WikipediaSearchResponse
            {
                Pages = new List<WikipediaPage>()
            };

            foreach (var item in searchData.Query.Search)
            {
                searchResults.Pages.Add(new WikipediaPage
                {
                    Id = item.PageId,
                    Title = item.Title,
                    Excerpt = item.Snippet.Replace("<span class=\"searchmatch\">", "").Replace("</span>", "")
                });
            }

            if (searchResults?.Pages == null || !searchResults.Pages.Any())
            {
                return new PluginResult<string>("No Wikipedia results found for the query.", true);
            }

            var topResult = searchResults.Pages.First();
            var summaryUrl = $"{_baseUrl}/page/summary/{Uri.EscapeDataString(topResult.Title)}";
            var summaryResponse = await _httpClient.GetAsync(summaryUrl, cancellationToken);

            WikipediaSummaryResponse? summaryResult = null;

            if (summaryResponse.IsSuccessStatusCode)
            {
                var summaryJson = await summaryResponse.Content.ReadAsStringAsync(cancellationToken);
                summaryResult = JsonConvert.DeserializeObject<WikipediaSummaryResponse>(summaryJson);
            }

            if (summaryResult == null)
            {
                var result = FormatSearchResults(searchResults);
                return new PluginResult<string>(result, true);
            }

            var formattedResult = FormatResults(topResult, summaryResult,
                searchResults.Pages.Skip(1).Take(limit - 1).ToList());
            return new PluginResult<string>(formattedResult, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Wikipedia Plugin Error: {ex}");
            return new PluginResult<string>("", false, $"Error: {ex.Message}");
        }
    }

    private string FormatResults(WikipediaPage mainResult, WikipediaSummaryResponse? summary,
        List<WikipediaPage> otherResults)
    {
        var result = new StringBuilder();

        result.AppendLine("# Wikipedia Results");

        if (summary != null)
        {
            result.AppendLine($"## {summary.Title}");

            if (!string.IsNullOrEmpty(summary.Description))
            {
                result.AppendLine($"**{summary.Description}**");
            }

            if (!string.IsNullOrEmpty(summary.Extract))
            {
                result.AppendLine(summary.Extract);
            }

            string pageUrl = summary.ContentUrls?.Desktop?.Page ??
                             $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(summary.Title)}";
            result.AppendLine($"[Read more on Wikipedia]({pageUrl})");
        }
        else
        {
            result.AppendLine($"## {mainResult.Title}");
            if (!string.IsNullOrEmpty(mainResult.Excerpt))
            {
                result.AppendLine(mainResult.Excerpt);
            }

            result.AppendLine(
                $"[Read more on Wikipedia](https://en.wikipedia.org/wiki/{Uri.EscapeDataString(mainResult.Title)})");
        }

        if (otherResults.Count > 0)
        {
            result.AppendLine("\n## Related Pages");
            foreach (var page in otherResults)
            {
                if (!string.IsNullOrEmpty(page.Title))
                {
                    result.AppendLine(
                        $"- **{page.Title}**: {(string.IsNullOrEmpty(page.Excerpt) ? "No description available" : page.Excerpt)}");
                }
            }
        }

        return result.ToString();
    }

    private string FormatSearchResults(WikipediaSearchResponse? response)
    {
        if (response?.Pages == null || response.Pages.Count == 0)
            return "No results found";

        var result = new StringBuilder();
        result.AppendLine("# Wikipedia Search Results:");

        for (int i = 0; i < response.Pages.Count; i++)
        {
            var page = response.Pages[i];
            if (!string.IsNullOrEmpty(page.Title))
            {
                string excerpt = string.IsNullOrEmpty(page.Excerpt) ? "No description available" : page.Excerpt;
                result.AppendLine(
                    $"- **{page.Title}**: {excerpt} ([Link](https://en.wikipedia.org/wiki/{Uri.EscapeDataString(page.Title)}))");
            }
        }

        if (result.ToString().Split('\n').Length <= 1)
        {
            return "No valid results found";
        }

        return result.ToString();
    }
}

public class MediaWikiSearchResponse
{
    public QueryContainer? Query { get; set; }
}

public class QueryContainer
{
    public List<SearchResult> Search { get; set; } = new List<SearchResult>();
}

public class SearchResult
{
    [JsonProperty("pageid")] public int PageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}

public class WikipediaSearchResponse
{
    public List<WikipediaPage> Pages { get; set; } = new List<WikipediaPage>();
}

public class WikipediaPage
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public WikipediaThumbnail? Thumbnail { get; set; }
}

public class WikipediaThumbnail
{
    public string? Source { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class WikipediaSummaryResponse
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Extract { get; set; } = string.Empty;
    public WikipediaThumbnail? Thumbnail { get; set; }
    public WikipediaContentUrls? ContentUrls { get; set; }
}

public class WikipediaContentUrls
{
    public WikipediaUrl? Desktop { get; set; }
    public WikipediaUrl? Mobile { get; set; }
}

public class WikipediaUrl
{
    public string? Page { get; set; }
    public string? Revisions { get; set; }
    public string? Edit { get; set; }
    public string? Talk { get; set; }
}