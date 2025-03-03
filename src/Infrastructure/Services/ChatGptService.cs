using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services;

public class ChatGptService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly double _inputTokenPricePer1K;
    private readonly double _outputTokenPricePer1K;
    private readonly string _modelCode;
    private readonly TokenCountingService _tokenCountingService;

    public ChatGptService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        double inputTokenPricePer1K,
        double outputTokenPricePer1K,
        string modelCode)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _apiKey = apiKey;
        _inputTokenPricePer1K = inputTokenPricePer1K;
        _outputTokenPricePer1K = outputTokenPricePer1K;
        _modelCode = modelCode;
        _tokenCountingService = new TokenCountingService();
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

    public Task<TokenUsage> CountTokensAsync(IEnumerable<MessageDto> messages)
    {
        // For a more accurate count, you could use the OpenAI tokenizer API
        // But for simplicity, we'll use our estimation service
        var inputTokens = _tokenCountingService.EstimateInputTokens(messages);

        // Estimate output tokens (this is just a rough estimate)
        var outputTokens = inputTokens / 2; // Assuming output is roughly half the input size

        var totalCost = _tokenCountingService.CalculateCost(
            inputTokens,
            outputTokens,
            _inputTokenPricePer1K,
            _outputTokenPricePer1K);

        return Task.FromResult(new TokenUsage(inputTokens, outputTokens, totalCost));
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