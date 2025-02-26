using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

public class ChatGptService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public ChatGptService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _apiKey = configuration["AI:ChatGPT:ApiKey"]!;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var messages = new List<OpenAiMessage>
        {
            new("system", "Always respond using markdown formatting")
        };
        messages.AddRange(history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new OpenAiMessage(m.IsFromAi ? "assistant" : "user", m.Content)));

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var requestBody = new
        {
            model = "gpt-4o",
            messages,
            max_tokens = 2000,
            stream = true
        };
        Console.WriteLine("Request Body: " + JsonSerializer.Serialize(requestBody));
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"OpenAI API Error: {response.StatusCode} - {errorContent}");
            yield return "Sorry, an error occurred while generating the response.";
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: ") && line.Trim() != "data: [DONE]")
            {
                var json = line["data: ".Length..];
                var chunk = JsonSerializer.Deserialize<OpenAiResponse>(json);
                if (chunk?.choices is { Length: > 0 } choices)
                {
                    var delta = choices[0].delta;
                    if (!string.IsNullOrEmpty(delta?.content))
                    {
                        yield return delta.content;
                    }
                }
            }
        }
    }

    private record OpenAiMessage(string role, string content);

    private record OpenAiResponse(
        string id,
        string @object,
        int created,
        string model,
        Choice[] choices
    );

    private record Choice(Delta delta, int index, string finish_reason);

    private record Delta(string content);
}