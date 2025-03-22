using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices;

public class GeminiService : BaseAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public GeminiService(
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
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var validHistory = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(2)
            .ToList();

        var contents = validHistory.Select(m => new
        {
            role = m.IsFromAi ? "model" : "user",
            parts = new[]{ new { text = m.Content } }
        }).ToArray();

        var generationConfig = new Dictionary<string, object>();

        if (AiModel?.MaxOutputTokens.HasValue == true)
        {
            generationConfig["maxOutputTokens"] = AiModel.MaxOutputTokens.Value;
        }

        if (ModelSettings != null)
        {
            generationConfig["temperature"] = ModelSettings.Temperature ?? 0.7;
            generationConfig["topP"] = ModelSettings.TopP ?? 0.8;
            generationConfig["topK"] = ModelSettings.TopK ?? 40;

            if (ModelSettings.StopSequences != null && ModelSettings.StopSequences.Any())
            {
                generationConfig["stopSequences"] = ModelSettings.StopSequences;
            }
        }
        else
        {
            generationConfig["temperature"] = 0.7;
            generationConfig["topP"] = 0.8;
            generationConfig["topK"] = 40;
        }

        return new { contents, generationConfig };
    }

    protected override HttpRequestMessage CreateRequest(object requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };
        return request;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history, CancellationToken cancellationToken)
    {
        var request = CreateRequest(CreateRequestBody(history));
        using var response =
            await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

       
            var responseArray = await JsonSerializer.DeserializeAsync<JsonElement?>(stream, cancellationToken: cancellationToken);

            if (responseArray.HasValue && responseArray.Value.ValueKind != JsonValueKind.Null)
            {
                foreach (var root in responseArray.Value.EnumerateArray())
                {
                    string? text = null;
                    int promptTokens = 0, outputTokens = 0;

                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0 &&
                            parts[0].TryGetProperty("text", out var textElement))
                        {
                            text = textElement.GetString();
                        }
                    }

                    if (root.TryGetProperty("usageMetadata", out var usageMetadata))
                    {
                        promptTokens = usageMetadata.GetProperty("promptTokenCount").GetInt32();
                        outputTokens = usageMetadata.TryGetProperty("candidatesTokenCount", out var outputTokenElement)
                            ? outputTokenElement.GetInt32()
                            : usageMetadata.GetProperty("totalTokenCount").GetInt32() - promptTokens;
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return new StreamResponse(text, promptTokens, outputTokens);
                    }
                }
            }
       
    }
}