using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices;

public class DeepSeekService : BaseAiService
{
    private const string BaseUrl = "https://api.deepseek.com/v1/";
    private const int DefaultMaxTokens = 4096; // Default for final response
    private const int MaxAllowedTokens = 8192; // Max for final response

    public DeepSeekService(
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
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }

    protected override string GetEndpointPath() => "chat/completions";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = new List<DeepSeekMessage>
        {
            new("system", "You are a helpful AI assistant.")
        };

        var mergedHistory = new List<MessageDto>();

        foreach (var message in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            if (mergedHistory.Count == 0 || mergedHistory[^1].IsFromAi != message.IsFromAi)
            {
                // Add a new message if the list is empty or the role changes
                mergedHistory.Add(message);
            }
            else
            {
                // Merge with the previous message
                var lastMessage = mergedHistory[^1];
                var updatedContent = lastMessage.Content + "\n" + message.Content;
                mergedHistory[^1] = lastMessage with { Content = updatedContent };
            }
        }

        messages.AddRange(mergedHistory
            .Select(m => new DeepSeekMessage(m.IsFromAi ? "assistant" : "user", m.Content)));

        // Determine max_tokens for the final response
        int maxTokens = AiModel?.MaxOutputTokens ?? DefaultMaxTokens;
        maxTokens = Math.Min(maxTokens, MaxAllowedTokens);

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["messages"] = messages,
            ["stream"] = true,
            ["max_tokens"] = maxTokens,
            ["enable_cot"] = true // Placeholder; replace with actual parameter if needed
        };

        var requestWithSettings = ApplyUserSettings(requestObj);
        return requestWithSettings;
    }
    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(CreateRequestBody(history));

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "DeepSeek");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var reasoningBuffer = new List<string>();
        var contentBuffer = new List<string>();
        DeepSeekUsage? finalUsage = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (reader.EndOfStream)
                break;

            var line = await reader.ReadLineAsync();
            if (cancellationToken.IsCancellationRequested)
                yield break;

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
                        // Yield final CoT and content with actual token usage
                        var reasoningContent = string.Join("", reasoningBuffer);
                        var finalContent = string.Join("", contentBuffer);
                        if (!string.IsNullOrEmpty(reasoningContent))
                        {
                            yield return new StreamResponse(reasoningContent, finalUsage.prompt_tokens, finalUsage.completion_tokens, IsThinking: true);
                        }
                        if (!string.IsNullOrEmpty(finalContent))
                        {
                            yield return new StreamResponse(finalContent, finalUsage.prompt_tokens, finalUsage.completion_tokens);
                        }
                        break;
                    }

                    if (delta?.reasoning_content != null)
                    {
                        reasoningBuffer.Add(delta.reasoning_content);
                        yield return new StreamResponse(delta.reasoning_content, finalUsage?.prompt_tokens ?? 0, finalUsage?.completion_tokens ?? 0, IsThinking: true);
                    }
                    else if (delta?.content != null)
                    {
                        contentBuffer.Add(delta.content);
                        yield return new StreamResponse(delta.content, finalUsage?.prompt_tokens ?? 0, finalUsage?.completion_tokens ?? 0);
                    }
                }
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

    private record DeepSeekChoice(DeepSeekDelta delta, int index, string? finish_reason);

    private record DeepSeekDelta(string? reasoning_content, string? content);

    private record DeepSeekUsage(int prompt_tokens, int completion_tokens, int total_tokens);
}