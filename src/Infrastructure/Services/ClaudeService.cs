using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class ClaudeService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public ClaudeService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _apiKey = configuration["AI:Claude:ApiKey"] ??
                  throw new ArgumentNullException("AI:Claude:ApiKey", "Claude API key not configured");
       
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var systemMessage =
            "Format your responses in markdown.";

        var messages = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ClaudeMessage(
                m.IsFromAi ? "assistant" : "user",
                m.Content.Trim()
            ))
            .TakeLast(10)
            .ToList();

        if (!messages.Any())
        {
            throw new ArgumentException("No valid messages in conversation history");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "messages");
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("x-api-key", _apiKey);

        var requestBody = new
        {
            model = "claude-3-5-sonnet-20241022",
            messages,
            system = systemMessage,
            max_tokens = 4000,
            temperature = 0.7,
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

       

        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();

            throw new ApplicationException(
                $"Claude API request failed ({response.StatusCode}): {errorContent}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: ") && line != "data: [DONE]")
            {
                var json = line["data: ".Length..];

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