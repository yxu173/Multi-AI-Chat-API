using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Serialization;

namespace Infrastructure.Services;

public class DeepSeekService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public DeepSeekService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.deepseek.com/v1/");
        _apiKey = configuration["AI:DeepSeek:ApiKey"]!;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var messages = history.Select(m => new DeepSeekMessage(
            m.IsFromAi ? "system" : "user",
            m.Content
        )).ToList();

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var requestBody = new
        {
            model = "deepseek-chat",
            messages,
            temperature = 1.5,
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

            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..].Trim();
                if (json == "[DONE]")
                {
                    break;
                }
                else
                {
                    var chunk = JsonSerializer.Deserialize<DeepSeekResponse>(json);
                    var content = chunk?.choices?.FirstOrDefault()?.delta?.content;

                    if (!string.IsNullOrEmpty(content))
                    {
                        yield return content;
                    }
                }
            }
        }
    }


    private record DeepSeekMessage(string role, string content);

    private record DeepSeekResponse(
        [property: JsonPropertyName("choices")]
        IEnumerable<DeepSeekChoice> choices
    );

    private record DeepSeekChoice(
        [property: JsonPropertyName("delta")] DeepSeekDelta delta
    );

    private record DeepSeekDelta(
        [property: JsonPropertyName("content")]
        string content
    );
}