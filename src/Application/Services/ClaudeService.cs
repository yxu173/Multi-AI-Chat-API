using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using IAiModelService = Application.Abstractions.Interfaces.IAiModelService;

namespace Application.Services;

public class ClaudeService : IAiModelService
{
    private const string CLAUDE_MODEL = "claude-3-opus-20240229";
    private readonly HttpClient _httpClient;
    private readonly string _claudeApiKey;
    private readonly ILogger<ClaudeService> _logger;

    public ClaudeService(IConfiguration configuration, HttpClient httpClient, ILogger<ClaudeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _claudeApiKey = configuration["AI:Claude:ApiKey"]!;

        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _claudeApiKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_claudeApiKey}");
    }

    public async Task<string> GetResponseAsync(string modelType, string message)
    {
        var requestBody = new
        {
            model = CLAUDE_MODEL,
            messages = new[]
            {
                new { role = "user", content = message }
            },
            max_tokens = 1024
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending request to Claude API with model: {Model}", CLAUDE_MODEL);

        var response = await _httpClient.PostAsync("messages", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        return responseContent;
    }

    public async IAsyncEnumerable<string> GetStreamingResponseAsync(string modelType, string message)
    {
        var requestBody = new
        {
            model = CLAUDE_MODEL,
            messages = new[]
            {
                new { role = "user", content = message }
            },
            max_tokens = 1024,
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending streaming request to Claude API with model: {Model}", CLAUDE_MODEL);

        // Log request headers
        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            _logger.LogInformation("Request Header - {Key}: {Value}", header.Key, string.Join(", ", header.Value));
        }

        var response = await _httpClient.PostAsync("messages", content);


        _logger.LogInformation("Response Status: {StatusCode}", response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Claude API Error Response: {Error}", errorContent);
        }

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

                var chunk = JsonSerializer.Deserialize<ClaudeStreamResponse>(line);
                if (chunk?.Type == "content_block_delta" && !string.IsNullOrEmpty(chunk.Delta?.Text))
                {
                    yield return chunk.Delta.Text;
                }
            }
        }
    }

    private class ClaudeStreamResponse
    {
        public string Type { get; set; }
        public DeltaContent Delta { get; set; }

        public class DeltaContent
        {
            public string Text { get; set; }
        }
    }
}