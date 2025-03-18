using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

namespace Infrastructure.Services.Plugins;

public class JinaWebPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _maxRetries;
    private readonly int _timeoutSeconds;
    private readonly bool _includeCached;

    public string Name => "Jina Web Search";
    public string Description => "Retrieve web content from URLs using Jina AI";

    public JinaWebPlugin(HttpClient httpClient, string apiKey, int maxRetries = 3, int timeoutSeconds = 30, bool includeCached = true)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _maxRetries = maxRetries;
        _timeoutSeconds = timeoutSeconds;
        _includeCached = includeCached;
        
        _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
    }

    public bool CanHandle(string userMessage)
    {
        return userMessage.StartsWith("/jina", StringComparison.OrdinalIgnoreCase) ||
               userMessage.StartsWith("/web", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract URL from user message
            var urlMatch = System.Text.RegularExpressions.Regex.Match(userMessage, @"(https?://\S+)");
            if (!urlMatch.Success)
            {
                return new PluginResult("", false, "No valid URL found in the message. Please include a URL starting with http:// or https://");
            }

            string url = urlMatch.Groups[1].Value;
            
            // Construct Jina AI request URL
            var jinaUrl = $"https://r.jina.ai/{url}";
            
            // Make the API call with retries
            HttpResponseMessage? response = null;
            int retries = 0;
            bool success = false;
            
            while (retries < _maxRetries && !success)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, jinaUrl);
                    request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                    
                    response = await _httpClient.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    success = true;
                }
                catch (Exception) when (retries < _maxRetries - 1)
                {
                    retries++;
                    await Task.Delay(1000 * retries, cancellationToken); // Exponential backoff
                }
            }
            
            if (!success || response == null)
            {
                throw new Exception("Failed to retrieve content after multiple attempts");
            }
            
            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);
            var jinaResponse = JsonConvert.DeserializeObject<JinaWebResponse>(contentString);
            
            if (jinaResponse == null)
            {
                return new PluginResult("", false, "Failed to parse response from Jina AI");
            }
            
            var result = FormatJinaResult(jinaResponse, url);
            return new PluginResult(result, true);
        }
        catch (Exception ex)
        {
            return new PluginResult("", false, $"Jina Web request failed: {ex.Message}");
        }
    }
    
    private string FormatJinaResult(JinaWebResponse response, string originalUrl)
    {
        var result = new StringBuilder();
        
        result.AppendLine($"## {response.Title}");
        result.AppendLine($"**Source:** {originalUrl}");
        
        if (!string.IsNullOrEmpty(response.Warning) && _includeCached)
        {
            result.AppendLine($"**Warning:** {response.Warning}");
        }
        
        result.AppendLine();
        result.AppendLine("### Content:");
        result.AppendLine(response.MarkdownContent);
        
        return result.ToString();
    }
}

public class JinaWebResponse
{
    [JsonProperty("Title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonProperty("URL Source")]
    public string UrlSource { get; set; } = string.Empty;
    
    [JsonProperty("Warning")]
    public string Warning { get; set; } = string.Empty;
    
    [JsonProperty("Markdown Content")]
    public string MarkdownContent { get; set; } = string.Empty;
}
