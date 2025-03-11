using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services.AiProvidersServices;

public class DeepSeekService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelCode;

    public DeepSeekService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.deepseek.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _apiKey = apiKey;
        _modelCode = modelCode;
    }

    public async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        Action<int, int>? tokenCallback = null)
    {
        var messages = new List<DeepSeekMessage>
        {
            new("system", "You are a helpful AI assistant.")
        };

        messages.AddRange(history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new DeepSeekMessage(m.IsFromAi ? "assistant" : "user", m.Content)));

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");

        var requestBody = new
        {
            model = _modelCode,
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
            throw new Exception($"DeepSeek API Error: {response.StatusCode} - {errorContent}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var contentBuffer = new List<string>();
        DeepSeekUsage? finalUsage = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                if (json == "[DONE]") break;

                var chunk = JsonSerializer.Deserialize<DeepSeekResponse>(json);
                if (chunk?.choices is { Length: > 0 } choices)
                {
                    var delta = choices[0].delta;

                    if (choices[0].finish_reason != null && chunk.usage != null)
                    {
                        finalUsage = chunk.usage;

                        foreach (var bufferedContent in contentBuffer)
                        {
                            yield return new StreamResponse(bufferedContent, finalUsage.prompt_tokens,
                                finalUsage.completion_tokens);
                        }

                        contentBuffer.Clear();
                    }

                    if (!string.IsNullOrEmpty(delta?.content))
                    {
                        if (finalUsage != null)
                        {
                            yield return new StreamResponse(delta.content, finalUsage.prompt_tokens,
                                finalUsage.completion_tokens);
                        }
                        else
                        {
                            contentBuffer.Add(delta.content);
                        }
                    }
                }
            }
        }

        if (finalUsage != null && contentBuffer.Count > 0)
        {
            foreach (var bufferedContent in contentBuffer)
            {
                yield return new StreamResponse(bufferedContent, finalUsage.prompt_tokens,
                    finalUsage.completion_tokens);
            }
        }
    }


    private record DeepSeekMessage(string role, string content);

    private record DeepSeekResponse(
        string id,
        string @object,
        int created,
        string model,
        DeepSeekChoice[] choices,
        DeepSeekUsage? usage = null
    );

    private record DeepSeekChoice(DeepSeekDelta delta, int index, string finish_reason);

    private record DeepSeekDelta(string content);

    private record DeepSeekUsage(int prompt_tokens, int completion_tokens, int total_tokens);
}