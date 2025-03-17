using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelCode;

    public AnthropicService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _apiKey = apiKey;
        _modelCode = modelCode;
    }

    public async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        Action<int, int>? tokenCallback = null)
    {
        var systemMessage = "Format your responses in markdown.";

        var messages = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ClaudeMessage(
                m.IsFromAi ? "assistant" : "user",
                m.Content.Trim()
            ))
            .TakeLast(10)
            .ToList();

        var request = new HttpRequestMessage(HttpMethod.Post, "messages");

        var requestBody = new
        {
            model = _modelCode,
            system = systemMessage,
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

        int inputTokens = 0;
        int outputTokens = 0;
        int estimatedOutputTokens = 0;

    
        StringBuilder fullResponse = new StringBuilder();
        HashSet<string> sentChunks = new HashSet<string>(); 

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                if (json == "[DONE]") break;

                using var doc = JsonDocument.Parse(json);
                var type = doc.RootElement.GetProperty("type").GetString();

                switch (type)
                {
                    case "message_start":
                        inputTokens = doc.RootElement
                            .GetProperty("message")
                            .GetProperty("usage")
                            .GetProperty("input_tokens")
                            .GetInt32();
                        tokenCallback?.Invoke(inputTokens, 0);
                        break;

                    case "content_block_delta":
                        var text = doc.RootElement
                            .GetProperty("delta")
                            .GetProperty("text")
                            .GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            fullResponse.Append(text);


                            estimatedOutputTokens = Math.Max(1, fullResponse.Length / 4);

                            yield return new StreamResponse(text, inputTokens, estimatedOutputTokens);

                            sentChunks.Add(text);

                            if (outputTokens == 0)
                            {
                                tokenCallback?.Invoke(inputTokens, estimatedOutputTokens);
                            }
                        }

                        break;

                    case "message_delta":
                        if (doc.RootElement.TryGetProperty("usage", out var usageElement) &&
                            usageElement.TryGetProperty("output_tokens", out var outputTokenElement))
                        {
                            outputTokens = outputTokenElement.GetInt32();
                            tokenCallback?.Invoke(inputTokens, outputTokens);
                        }

                        break;
                }
            }
        }

        if (outputTokens == 0 && estimatedOutputTokens > 0)
        {
            tokenCallback?.Invoke(inputTokens, estimatedOutputTokens);
        }
    }


    private record ClaudeMessage(string role, string content);

    private record ClaudeUsage(int input_tokens, int output_tokens);

    private record ClaudeResponse(
        string type,
        ClaudeDelta? delta,
        ClaudeUsage? usage
    );

    private record ClaudeDelta(string text);
}