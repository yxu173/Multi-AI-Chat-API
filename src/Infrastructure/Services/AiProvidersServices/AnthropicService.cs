using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Infrastructure.Services.AiProvidersServices.Base;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Infrastructure.Services.AiProvidersServices;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";

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
        var systemMessage = "Format your responses in markdown.";
        var messages = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ClaudeMessage(m.IsFromAi ? "assistant" : "user", m.Content.Trim()))
            .ToList();

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["system"] = systemMessage,
            ["messages"] = messages,
            ["stream"] = true
        };

        var requestWithSettings = ApplyUserSettings(requestObj, false);

        requestWithSettings.Remove("frequency_penalty");
        requestWithSettings.Remove("presence_penalty");
        requestWithSettings.Remove("maxOutputTokens");
        requestWithSettings.Remove("max_output_tokens");

        if (ModelSettings?.StopSequences != null && ModelSettings.StopSequences.Any())
        {
            requestWithSettings["stop_sequences"] = ModelSettings.StopSequences;
        }

        return requestWithSettings;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await HttpClient.SendAsync(
            CreateRequest(CreateRequestBody(history)),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        int inputTokens = 0;
        int estimatedOutputTokens = 0;
        StringBuilder fullResponse = new StringBuilder();

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

                    case "content_block_delta":
                        if (doc.RootElement.TryGetProperty("delta", out var deltaElement) &&
                            deltaElement.TryGetProperty("text", out var textElement))
                        {
                            var text = textElement.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                fullResponse.Append(text);
                                estimatedOutputTokens = Math.Max(1, fullResponse.Length / 4);
                                yield return new StreamResponse(text, inputTokens, estimatedOutputTokens);
                            }
                        }
                        break;
                }
            }
        }
    }

    private record ClaudeMessage(string role, string content);
}