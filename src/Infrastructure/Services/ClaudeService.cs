using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services;

public class ClaudeService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly double _inputTokenPricePer1K;
    private readonly double _outputTokenPricePer1K;
    private readonly TokenCountingService _tokenCountingService;

    public ClaudeService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        double inputTokenPricePer1K,
        double outputTokenPricePer1K)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _apiKey = apiKey;
        _inputTokenPricePer1K = inputTokenPricePer1K;
        _outputTokenPricePer1K = outputTokenPricePer1K;
        _tokenCountingService = new TokenCountingService();
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var messages = history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new ClaudeMessage(
                m.IsFromAi ? "assistant" : "user",
                m.Content
            ))
            .ToList();

        var request = new HttpRequestMessage(HttpMethod.Post, "messages");
        
        var requestBody = new
        {
            model = "claude-3-sonnet-20240229",
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

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                if (json == "[DONE]") break;

              
                    var chunk = JsonSerializer.Deserialize<ClaudeResponse>(json);
                    if (chunk?.delta?.text is { Length: > 0 } textChunk)
                    {
                        yield return textChunk;
                    }
               
            }
        }
    }
    
    public async Task<TokenUsage> CountTokensAsync(IEnumerable<MessageDto> messages)
    {
        var inputTokens = _tokenCountingService.EstimateInputTokens(messages);
        var outputTokens = inputTokens / 2; // Rough estimate
        
        var totalCost = _tokenCountingService.CalculateCost(
            inputTokens,
            outputTokens,
            _inputTokenPricePer1K,
            _outputTokenPricePer1K);
            
        return new TokenUsage(inputTokens, outputTokens, totalCost);
    }

        private record ClaudeMessage(string role, string content);
    
    private record ClaudeResponse(string type, ClaudeDelta delta);
    
    private record ClaudeDelta(string text);
}