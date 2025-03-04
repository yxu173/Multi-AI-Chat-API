using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services;

public class GeminiService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelCode;

    public GeminiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
        _apiKey = apiKey;
        _modelCode = modelCode;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var contents = new List<GeminiContent>();

        foreach (var message in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            contents.Add(new GeminiContent(
                parts: new[] { new GeminiPart(text: message.Content) },
                role: message.IsFromAi ? "model" : "user"
            ));
        }

        var requestBody = new
        {
            contents,
            generationConfig = new
            {
                maxOutputTokens = 2048,
            },
            streamGenerationConfig = new
            {
                streamContentTokens = true
            }
        };

        var endpoint = $"v1beta/models/{_modelCode}:streamGenerateContent?key={_apiKey}";

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API Error: {response.StatusCode} - {errorContent}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;


            var chunk = JsonSerializer.Deserialize<GeminiResponse>(line);
            if (chunk?.candidates is { Length: > 0 } candidates)
            {
                var content = candidates[0].content;
                if (content?.parts is { Length: > 0 } parts)
                {
                    var text = parts[0].text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }
            }
        }
    }

    private record GeminiContent(GeminiPart[] parts, string role);

    private record GeminiPart(string text);

    private record GeminiResponse(GeminiCandidate[] candidates);

    private record GeminiCandidate(GeminiContent content, string finishReason);
}