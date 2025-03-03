using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services;

public class ClaudeService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public ClaudeService(
        IHttpClientFactory httpClientFactory,
        string apiKey)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _apiKey = apiKey;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var messages = history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new ClaudeMessage(
                m.IsFromAi ? "assistant" : "user",
                m.Content
            ))
            .ToList();

        var request = new HttpRequestMessage(HttpMethod.Post, "messages");
        
        var requestBody = new
        {
            model = "claude-3-sonnet-20240229",
            messages,
            max_tokens = 2000,
            stream = true
        };
        
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), 
            Encoding.UTF8, 
            "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Claude API Error: {response.StatusCode} - {errorContent}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                if (json == "[DONE]") break;

              
                    var chunk = JsonSerializer.Deserialize<ClaudeResponse>(json);
                    if (chunk?.delta?.text is { Length: > 0 } textChunk)
                    {
                        yield return textChunk;
                    }
               
            }
        }
    }

        private record ClaudeMessage(string role, string content);
    
    private record ClaudeResponse(string type, ClaudeDelta delta);
    
    private record ClaudeDelta(string text);
}