using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json.Linq;

namespace Infrastructure.Services.Plugins;

public class HackerNewsSearchPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "http://hn.algolia.com/api/v1";

    // Tool name and description for AI
    public string Name => "search_hacker_news";
    public string Description => "Search Hacker News posts and comments using various filters like relevance, date, tags and more.";

    public HackerNewsSearchPlugin(HttpClient httpClient)
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
              "description": "Full-text search query"
            },
            "sort_by_date": {
              "type": "boolean",
              "description": "If true, sort by date (most recent first). If false, sort by relevance (default).",
              "default": false
            },
            "tags": {
              "type": "string",
              "description": "Filter on specific tags. Available tags: story, comment, poll, pollopt, show_hn, ask_hn, front_page, author_USERNAME, story_ID",
              "default": ""
            },
            "numeric_filters": {
              "type": "string",
              "description": "Filter on specific numerical fields with operators: =, >, <, >=, <=. Available fields: created_at_i, points, num_comments. Example: 'points>10,num_comments<5'",
              "default": ""
            },
            "page": {
              "type": "integer",
              "description": "Page number for pagination",
              "default": 0
            }
          },
          "required": ["query"]
        }
        """;
        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    public async Task<PluginResult> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        if (arguments == null || !arguments.TryGetPropertyValue("query", out var queryNode) || 
            queryNode is not JsonValue queryValue || queryValue.GetValueKind() != JsonValueKind.String)
        {
            return new PluginResult("", false, "Missing or invalid 'query' argument for Hacker News search.");
        }

        string query = queryValue.GetValue<string>();
        
        // Optional parameters with defaults
        bool sortByDate = false;
        if (arguments.TryGetPropertyValue("sort_by_date", out var sortByDateNode) && 
            sortByDateNode is JsonValue sortByDateValue && sortByDateValue.GetValueKind() == JsonValueKind.True)
        {
            sortByDate = true;
        }
        
        string tags = "";
        if (arguments.TryGetPropertyValue("tags", out var tagsNode) && 
            tagsNode is JsonValue tagsValue && tagsValue.GetValueKind() == JsonValueKind.String)
        {
            tags = tagsValue.GetValue<string>();
        }
        
        string numericFilters = "";
        if (arguments.TryGetPropertyValue("numeric_filters", out var filtersNode) && 
            filtersNode is JsonValue filtersValue && filtersValue.GetValueKind() == JsonValueKind.String)
        {
            numericFilters = filtersValue.GetValue<string>();
        }
        
        int page = 0;
        if (arguments.TryGetPropertyValue("page", out var pageNode) && 
            pageNode is JsonValue pageValue && pageValue.GetValueKind() == JsonValueKind.Number)
        {
            page = pageValue.TryGetValue<int>(out var pageInt) ? pageInt : 0;
        }

        try
        {
            // Build the request URL based on parameters
            string endpoint = sortByDate ? "search_by_date" : "search";
            string requestUrl = $"{BaseUrl}/{endpoint}?query={Uri.EscapeDataString(query)}";
            
            if (!string.IsNullOrEmpty(tags))
            {
                requestUrl += $"&tags={Uri.EscapeDataString(tags)}";
            }
            
            if (!string.IsNullOrEmpty(numericFilters))
            {
                requestUrl += $"&numericFilters={Uri.EscapeDataString(numericFilters)}";
            }
            
            requestUrl += $"&page={page}";
            
            // Make the request
            var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var formattedResult = FormatSearchResults(jsonResponse);
            
            return new PluginResult(formattedResult, true);
        }
        catch (Exception ex)
        {
            return new PluginResult("", false, $"Hacker News search failed: {ex.Message}");
        }
    }

    private string FormatSearchResults(string jsonResponse)
    {
        try
        {
            // Parse the JSON response
            var content = JObject.Parse(jsonResponse);
            var hits = content["hits"] as JArray;
            
            if (hits == null || !hits.Any())
            {
                return "No results found.";
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Hacker News Search Results");
            sb.AppendLine();
            
            int count = 1;
            foreach (var hit in hits.Take(10)) // Limit to top 10 results
            {
                string title = hit["title"]?.ToString() ?? hit["story_title"]?.ToString() ?? "[No Title]";
                string author = hit["author"]?.ToString() ?? "[Unknown]";
                string type = hit["_tags"]?.First()?.ToString() ?? "item";
                string url = hit["url"]?.ToString();
                string points = hit["points"]?.ToString() ?? "0";
                string commentCount = hit["num_comments"]?.ToString() ?? "0";
                string itemId = hit["objectID"]?.ToString();
                
                // Convert Unix timestamp to readable date if available
                string createdAt = "[Unknown Date]";
                if (hit["created_at_i"] != null && long.TryParse(hit["created_at_i"].ToString(), out long timestamp))
                {
                    var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    createdAt = date.ToString("yyyy-MM-dd HH:mm");
                }
                
                sb.AppendLine($"## {count}. {title}");
                sb.AppendLine($"**Type:** {type} | **Author:** {author} | **Date:** {createdAt}");
                sb.AppendLine($"**Points:** {points} | **Comments:** {commentCount}");
                
                if (!string.IsNullOrEmpty(url))
                {
                    sb.AppendLine($"**URL:** {url}");
                }
                
                if (!string.IsNullOrEmpty(itemId))
                {
                    sb.AppendLine($"**HN Link:** https://news.ycombinator.com/item?id={itemId}");
                }
                
                if (hit["comment_text"] != null)
                {
                    var commentText = hit["comment_text"].ToString();
                    // Truncate long comments
                    if (commentText.Length > 300)
                    {
                        commentText = commentText.Substring(0, 300) + "...";
                    }
                    sb.AppendLine($"**Comment:** {commentText}");
                }
                
                sb.AppendLine();
                count++;
            }
            
            // Add pagination info
            var page = content["page"]?.Value<int>() ?? 0;
            var totalPages = content["nbPages"]?.Value<int>() ?? 0;
            var totalHits = content["nbHits"]?.Value<int>() ?? 0;
            
            sb.AppendLine($"**Page {page + 1} of {totalPages}** | Total results: {totalHits}");
            
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error parsing search results: {ex.Message}\n\nRaw response: {jsonResponse}";
        }
    }
}
