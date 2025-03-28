using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

public class GeminiService : BaseAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public GeminiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
    }

    protected override void ConfigureHttpClient()
    {
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override List<(string Role, string Content)> PrepareMessageList(IEnumerable<MessageDto> history)
    {
        var messages = base.PrepareMessageList(history);
        return messages;
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history);
        var contents = messages.Select(m => new
        {
            role = m.Role == "assistant" ? "model" : m.Role == "system" ? "user" : "user",
            parts = new[] { new { text = m.Content } }
        }).ToList();

        var parameters = GetModelParameters();
        var generationConfig = new Dictionary<string, object>();


        var supportedParams = new HashSet<string>() { "temperature", "topP", "topK", "maxOutputTokens" };


        if (parameters.ContainsKey("temperature")) generationConfig["temperature"] = parameters["temperature"];
        if (parameters.ContainsKey("top_p")) generationConfig["topP"] = parameters["top_p"];
        if (parameters.ContainsKey("top_k")) generationConfig["topK"] = parameters["top_k"];
        if (parameters.ContainsKey("max_tokens")) generationConfig["maxOutputTokens"] = parameters["max_tokens"];
        if (parameters.ContainsKey("stop")) generationConfig["stopSequences"] = parameters["stop"];


        var keysToRemove = generationConfig.Keys.Where(k => !supportedParams.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            Console.WriteLine($"Removing unsupported parameter for Gemini: {key}");
            generationConfig.Remove(key);
        }

        var requestObj = new { contents, generationConfig };
        return requestObj;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        var request = CreateRequest(requestBody);

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to Gemini: {ex.Message}");
            throw;
        }

        int maxRetries = 3;
        int retryCount = 0;

        while (!response.IsSuccessStatusCode && retryCount < maxRetries)
        {
            retryCount++;

            try
            {
                // Extract error details
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Gemini API Error (attempt {retryCount}): {errorContent}");

                var simpleRequestBody = new
                {
                    contents = ((dynamic)requestBody).contents,
                    generationConfig = new Dictionary<string, object>()
                };

                Console.WriteLine($"Retrying Gemini request with minimal parameters (attempt {retryCount})");
                var retryRequest = CreateRequest(simpleRequestBody);
                response = await HttpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during auto-correction attempt {retryCount}: {ex.Message}");
                break;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Gemini");
            yield break;
        }

        var fullResponse = new StringBuilder();
        int promptTokens = 0;
        int outputTokens = 0;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);


        var responseArray =
            await JsonSerializer.DeserializeAsync<JsonElement?>(stream, cancellationToken: cancellationToken);
        if (responseArray.HasValue)
        {
            foreach (var root in responseArray.Value.EnumerateArray())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string? text = null;
                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates[0].TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts[0].TryGetProperty("text", out var textElement))
                {
                    text = textElement.GetString();
                }

                // Extract token counts from usageMetadata
                if (root.TryGetProperty("usageMetadata", out var usageMetadata))
                {
                    if (usageMetadata.TryGetProperty("promptTokenCount", out var promptTokenElement))
                    {
                        promptTokens = promptTokenElement.GetInt32();
                    }

                    if (usageMetadata.TryGetProperty("candidatesTokenCount", out var outputTokenElement))
                    {
                        outputTokens = outputTokenElement.GetInt32();
                    }
                    else if (usageMetadata.TryGetProperty("totalTokenCount", out var totalTokenElement))
                    {
                        var totalTokens = totalTokenElement.GetInt32();
                        outputTokens = totalTokens - promptTokens;
                    }
                }

                if (!string.IsNullOrEmpty(text))
                {
                    fullResponse.Append(text);
                    yield return new StreamResponse(text, promptTokens, outputTokens);
                }
            }
        }
    }
}