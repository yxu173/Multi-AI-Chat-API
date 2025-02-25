using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

public class ClaudeService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public ClaudeService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _apiKey = configuration["AI:Claude:ApiKey"];
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var messages = new List<ClaudeMessage>
        {
            new("system", "Always respond using markdown formatting")
        };
        messages.AddRange(history.Select(m => new ClaudeMessage(
            m.IsFromAi ? "assistant" : "user",
            m.Content
        )).ToList());

        var request = new HttpRequestMessage(HttpMethod.Post, "messages");
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("x-api-key", _apiKey);

        var requestBody = new
        {
            model = "claude-3-opus-20240229",
            messages,
            max_tokens = 2000,
            stream = true
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

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