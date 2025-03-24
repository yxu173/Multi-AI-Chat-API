using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultMaxTokens = 20000; // Default for final response
    private const int MaxAllowedTokens = 32768; // Claude's max limit
    private const int DefaultThinkingBudget = 16000; // Default thinking budget
    private readonly ILogger<AnthropicService>? _logger;

    public AnthropicService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        Domain.Aggregates.Users.UserAiModelSettings? modelSettings = null,
        AiModel? aiModel = null,
        ModelParameters? customModelParameters = null,
        ILogger<AnthropicService>? logger = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
        _logger = logger;
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

        // Set thinking budget: Use DefaultThinkingBudget (16000) or specified value
        int thinkingBudget = DefaultThinkingBudget;

        // Allow custom parameters to override thinking budget if specified
        if (CustomModelParameters?.ReasoningEffort.HasValue == true)
        {
            // Map reasoning effort to thinking budget by scaling from 0-100 to 1000-24000
            thinkingBudget = Math.Min(24000, 1000 + (CustomModelParameters.ReasoningEffort.Value * 230));
        }

        // Check if thinking should be enabled using the base class method
        bool enableThinking = ShouldEnableThinking();

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["max_tokens"] = maxTokens,
            ["stream"] = true
        };

        // Only add thinking configuration if enabled and model supports it
        if (enableThinking && AiModel?.SupportsThinking == true)
        {
            // For Anthropic, when thinking is enabled, temperature MUST be 1.0
            requestObj["temperature"] = 1.0;

            // For Anthropic, we use a minimal thinking object structure to avoid nesting issues
            requestObj["thinking"] = new Dictionary<string, object>
            {
                ["type"] = "enabled",
                ["budget_tokens"] = thinkingBudget
            };
        }

        requestObj["messages"] = messages;

        // Add system message separately if present
        var systemMessage = GetSystemMessage();
        if (!string.IsNullOrEmpty(systemMessage))
        {
            requestObj["system"] = systemMessage;
        }

        // Apply user settings (e.g., temperature, top_p, etc.) - but don't overwrite temperature when thinking is enabled
        var requestWithSettings = ApplyUserSettings(requestObj, false, !enableThinking);

        // Remove unsupported parameters for Anthropic
        requestWithSettings.Remove("frequency_penalty");
        requestWithSettings.Remove("presence_penalty");
        requestWithSettings.Remove("maxOutputTokens");
        requestWithSettings.Remove("max_output_tokens");
        requestWithSettings.Remove("top_k");
        requestWithSettings.Remove("top_p");
        requestWithSettings.Remove("topP");
        requestWithSettings.Remove("enable_thinking");
        requestWithSettings.Remove("enable_cot");
        requestWithSettings.Remove("enable_reasoning");
        requestWithSettings.Remove("reasoning_mode");
        requestWithSettings.Remove("context_limit");
        requestWithSettings.Remove("safety_settings");
        requestWithSettings.Remove("stop_sequences");

        // Log the final request structure
        _logger?.LogDebug("Anthropic API request: {Request}", JsonSerializer.Serialize(requestWithSettings));

        return requestWithSettings;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        var request = CreateRequest(requestBody);

        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Anthropic");
            yield break;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(contentStream);

        var contentBlockTypes = new Dictionary<int, string>();
        var inputTokens = 0;
        var estimatedOutputTokens = 0;
        var fullResponse = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line;

            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading response stream line");
                break;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            StreamResponse? streamResponse = null;

            try
            {
                // Try to extract a StreamResponse from the json
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
                                if (blockType == "thinking" &&
                                    deltaElement.TryGetProperty("thinking", out var thinkingElement))
                                {
                                    var thinkingText = thinkingElement.GetString();
                                    if (!string.IsNullOrEmpty(thinkingText))
                                    {
                                        streamResponse = new StreamResponse(thinkingText, inputTokens, 0,
                                            IsThinking: true);
                                    }
                                }
                                else if (blockType == "text" &&
                                         deltaElement.TryGetProperty("text", out var textElement))
                                {
                                    var text = textElement.GetString();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        fullResponse.Append(text);
                                        estimatedOutputTokens = Math.Max(1, fullResponse.Length / 4);
                                        streamResponse = new StreamResponse(text, inputTokens, estimatedOutputTokens);
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing response JSON: {Json}", json);
            }

            // If we extracted a valid StreamResponse, yield it
            if (streamResponse != null)
            {
                yield return streamResponse;
            }
        }
    }

    private record ClaudeMessage(string role, string content);
}