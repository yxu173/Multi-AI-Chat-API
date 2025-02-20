using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using IAiModelService = Application.Abstractions.Interfaces.IAiModelService;

namespace Application.Services;

public class ChatGPTService : IAiModelService
{
    private const string GPT_MODEL = "gpt-4-turbo-preview";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public ChatGPTService(IConfiguration configuration, HttpClient httpClient)
    {
        _httpClient = httpClient;
        _apiKey = configuration["AI:ChatGPT:ApiKey"]
                  ?? throw new ArgumentNullException("OpenAI API key is not configured");
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<string> GetResponseAsync(string modelType, string message)
    {
        var requestBody = new
        {
            model = GPT_MODEL,
            messages = new[]
            {
                new { role = "user", content = message }
            },
            stream = false
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("chat/completions", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(result);

        return jsonDocument.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    public async IAsyncEnumerable<string> GetStreamingResponseAsync(string modelType, string message)
    {
        var requestBody = new
        {
            model = GPT_MODEL,
            messages = new[]
            {
                new { role = "user", content = message }
            },
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("chat/completions", content);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("data: "))
            {
                line = line.Substring("data: ".Length);
                if (line == "[DONE]") break;

                var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(line);
                if (chunk?.Choices?.FirstOrDefault()?.Delta?.Content != null)
                {
                    yield return chunk.Choices[0].Delta.Content;
                }
            }
        }
    }

    private class ChatCompletionChunk
    {
        public List<Choice> Choices { get; set; }

        public class Choice
        {
            public Delta Delta { get; set; }
        }

        public class Delta
        {
            public string Content { get; set; }
        }
    }
}