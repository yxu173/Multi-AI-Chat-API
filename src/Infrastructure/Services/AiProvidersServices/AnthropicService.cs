using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";

    public AnthropicService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
    }

    protected override string GetEndpointPath() => "messages";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
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

        return new
        {
            model = ModelCode,
            system = systemMessage,
            messages,
            max_tokens = 2000,
            stream = true
        };
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var request = CreateRequest(CreateRequestBody(history));
        
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Claude");
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
                        }
                        break;

                    case "message_delta":
                        if (doc.RootElement.TryGetProperty("usage", out var usageElement) &&
                            usageElement.TryGetProperty("output_tokens", out var outputTokenElement))
                        {
                            outputTokens = outputTokenElement.GetInt32(); }
                        break;
                }
            }
        }
    }

    private record ClaudeMessage(string role, string content);
}