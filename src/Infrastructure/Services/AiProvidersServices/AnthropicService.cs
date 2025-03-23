using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Infrastructure.Services.AiProvidersServices.Base;
using System.Threading;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultMaxTokens = 4096; // Safe default below 8192
    private const int MaxAllowedTokens = 8192; // Anthropic's max for Claude 3.5 Sonnet
    private const int DefaultThinkingBudget = 4096; // Default budget for thinking trace

    public AnthropicService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        Domain.Aggregates.Users.UserAiModelSettings? modelSettings = null,
        AiModel? aiModel = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel)
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
        var messages = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ClaudeMessage(m.IsFromAi ? "assistant" : "user", m.Content.Trim()))
            .ToList();

        // Determine max_tokens: Use AiModel.MaxOutputTokens if available, otherwise default
        int maxTokens = AiModel?.MaxOutputTokens ?? DefaultMaxTokens;
        maxTokens = Math.Min(maxTokens, MaxAllowedTokens);

        // Set thinking budget: Half of max_tokens or default, capped at max allowed
        int thinkingBudget = Math.Min(DefaultThinkingBudget, maxTokens / 2);

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["max_tokens"] = maxTokens,
            ["stream"] = true,
            ["thinking"] = new Dictionary<string, object>
            {
                { "type", "enabled" },
                { "budget_tokens", thinkingBudget }
            },
            ["messages"] = messages
        };

        // Add system message separately if present
        var systemMessage = GetSystemMessage();
        if (!string.IsNullOrEmpty(systemMessage))
        {
            requestObj["system"] = systemMessage;
        }

        // Apply user settings (e.g., temperature, top_p, etc.)
        var requestWithSettings = ApplyUserSettings(requestObj, false);
        requestWithSettings["temperature"] = 1.0;
        // Remove unsupported parameters for Anthropic
        requestWithSettings.Remove("frequency_penalty");
        requestWithSettings.Remove("presence_penalty");
        requestWithSettings.Remove("maxOutputTokens");
        requestWithSettings.Remove("max_output_tokens");
        requestWithSettings.Remove("top_k");
        requestWithSettings.Remove("top_p");

        return requestWithSettings;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(CreateRequestBody(history));
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Anthropic");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var contentBlockTypes = new Dictionary<int, string>();
        int inputTokens = 0;
        int estimatedOutputTokens = 0;
        var fullResponse = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
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
                        if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                            messageElement.TryGetProperty("usage", out var usageElement) &&
                            usageElement.TryGetProperty("input_tokens", out var inputTokensElement))
                        {
                            inputTokens = inputTokensElement.GetInt32();
                        }
                        break;

                    case "content_block_start":
                        if (doc.RootElement.TryGetProperty("index", out var indexElement) &&
                            doc.RootElement.TryGetProperty("content_block", out var contentBlockElement))
                        {
                            var index = indexElement.GetInt32();
                            var blockType = contentBlockElement.GetProperty("type").GetString();
                            contentBlockTypes[index] = blockType;
                        }
                        break;

                    case "content_block_delta":
                        if (doc.RootElement.TryGetProperty("index", out var deltaIndexElement) &&
                            doc.RootElement.TryGetProperty("delta", out var deltaElement))
                        {
                            var deltaIndex = deltaIndexElement.GetInt32();
                            if (contentBlockTypes.TryGetValue(deltaIndex, out var blockType))
                            {
                                if (blockType == "thinking" && deltaElement.TryGetProperty("thinking", out var thinkingElement))
                                {
                                    var thinkingText = thinkingElement.GetString();
                                    if (!string.IsNullOrEmpty(thinkingText))
                                    {
                                        yield return new StreamResponse(thinkingText, inputTokens, 0, IsThinking: true);
                                    }
                                }
                                else if (blockType == "text" && deltaElement.TryGetProperty("text", out var textElement))
                                {
                                    var text = textElement.GetString();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        fullResponse.Append(text);
                                        estimatedOutputTokens = Math.Max(1, fullResponse.Length / 4);
                                        yield return new StreamResponse(text, inputTokens, estimatedOutputTokens);
                                    }
                                }
                            }
                        }
                        break;

                    case "message_stop":
                        if (doc.RootElement.TryGetProperty("message", out var finalMessage) &&
                            finalMessage.TryGetProperty("usage", out var finalUsage) &&
                            finalUsage.TryGetProperty("output_tokens", out var outputTokensElement))
                        {
                            estimatedOutputTokens = outputTokensElement.GetInt32();
                        }
                        break;
                }
            }
        }
    }

    private record ClaudeMessage(string role, string content);
}