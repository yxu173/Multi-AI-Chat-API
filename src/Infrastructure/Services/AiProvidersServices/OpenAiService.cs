using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelCode;

    public OpenAiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _modelCode = modelCode;
    }


    public async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        Action<int, int>? tokenCallback = null)
    {
        var messages = new List<OpenAiMessage>
        {
            new("system", "Always respond using markdown formatting")
        };

        messages.AddRange(history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new OpenAiMessage(m.IsFromAi ? "assistant" : "user", m.Content)));

        var tokenizer = Tiktoken.ModelToEncoder.For(_modelCode);

        int inputTokens = 0;
        foreach (var msg in messages)
        {
            inputTokens += tokenizer.Encode(msg.content).Count;
        }
        tokenCallback?.Invoke(inputTokens, 0);

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
            throw new Exception($"OpenAI API Error: {response.StatusCode} - {errorContent}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        int outputTokens = 0;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("data: ") && line.Trim() != "data: [DONE]")
            {
                var json = line["data: ".Length..];

                var chunk = JsonSerializer.Deserialize<OpenAiResponse>(json);
                if (chunk?.choices is { Length: > 0 } choices)
                {
                    var delta = choices[0].delta;
                    if (!string.IsNullOrEmpty(delta?.content))
                    {
                        outputTokens += tokenizer.Encode(delta.content).Count;
                        tokenCallback?.Invoke(inputTokens, outputTokens);
                        yield return new StreamResponse(delta.content, inputTokens, outputTokens);
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
