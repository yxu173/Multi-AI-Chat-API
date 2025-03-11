using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services.Plugins;

public class PerplexityPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _perplexityApiKey;

    public string Id => "perplexity";
    public string Name => "Perplexity";
    public string Description => "Uses Perplexity AI for enhanced information retrieval";

    public PerplexityPlugin(HttpClient httpClient,  string perplexityApiKey)
    {
        _httpClient = httpClient;
        _perplexityApiKey = perplexityApiKey;
    }

    public bool CanHandle(string userMessage)
    {
     
        return userMessage.ToLower().Contains("perplexity") ||
               (userMessage.ToLower().Contains("research") && userMessage.Length > 50);
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = ExtractQuery(userMessage);
            var perplexityResponse = await QueryPerplexityApi(query, cancellationToken);

            return new PluginResult(
                $"Perplexity AI Research Results:\n\n{perplexityResponse}",
                true,
                Name
            );
        }
        catch (Exception ex)
        {
            return new PluginResult(
                "Unable to retrieve information from Perplexity at this time.",
                false,
                ex.Message
            );
        }
    }

    private string ExtractQuery(string userMessage)
    {
        // Remove any explicit references to Perplexity
        var query = Regex.Replace(userMessage, @"(?i)using perplexity|with perplexity|perplexity|research", "",
            RegexOptions.IgnoreCase).Trim();
        return query;
    }

    private async Task<string> QueryPerplexityApi(string query, CancellationToken cancellationToken)
    {
       
        var requestData = new
        {
            query = query,
            max_tokens = 2000
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestData),
            Encoding.UTF8,
            "application/json"
        );

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/v1/query")
        {
            Content = content
        };

        request.Headers.Add("Authorization", $"Bearer {_perplexityApiKey}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonDocument.Parse(jsonResponse);

      
        var responseText = result.RootElement.GetProperty("response").GetString();

        return responseText;
    }
}