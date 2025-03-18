using Application.Abstractions.Interfaces;

namespace Infrastructure.Services.Plugins;

public class WebPageReader : IChatPlugin
{
   private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public string Name => "Jina AI Web Reader";
    public string Description => "Fetches and summarizes web content using Jina AI";

    public WebPageReader(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _httpClient.BaseAddress = new Uri("https://r.jina.ai/");
    }

    public bool CanHandle(string userMessage)
    {
        return userMessage.StartsWith("/jina ", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = userMessage.Replace("/jina ", "").Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                return new PluginResult("", false, "Invalid URL provided");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var formatted = FormatResponse(url, content);
            return new PluginResult(formatted, true);
        }
        catch (Exception ex)
        {
            return new PluginResult("", false, $"Jina AI request failed: {ex.Message}");
        }
    }

    private string FormatResponse(string url, string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var title = lines.FirstOrDefault(l => l.StartsWith("Title:"))?.Replace("Title: ", "") ?? "Untitled";
        return $"**Title:** {title}\n\n" +
               $"**URL Source:** {url}\n\n" +
               "**Warning:** This is a cached snapshot of the original page, consider retry with caching opt-out.\n\n" +
               "**Markdown Content:**\n" +
               string.Join("\n", lines.SkipWhile(l => !l.StartsWith("Markdown Content:")).Skip(1));
    }
}