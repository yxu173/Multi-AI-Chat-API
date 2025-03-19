using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices
{
    public class GeminiService : BaseAiService
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/";

        public GeminiService(
            IHttpClientFactory httpClientFactory,
            string apiKey,
            string modelCode)
            : base(httpClientFactory, apiKey, modelCode, BaseUrl)
        {
        }

        protected override void ConfigureHttpClient()
        {
            // No additional headers needed for base initialization
            // API key is passed in the URL for Google's Gemini API
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
                parts = new[] { new { text = m.Content } }
            }).ToArray();

            return new
            {
                contents,
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 2048,
                    topP = 0.8,
                    topK = 40
                }
            };
        }

        // Override the CreateRequest method since Gemini has a unique request format
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

        public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history)
        {
            var request = CreateRequest(CreateRequestBody(history));

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            try 
            {
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                await HandleApiErrorAsync(response, "Gemini");
                yield break;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    string? text = null;
                    int promptTokens = 0;
                    int outputTokens = 0;

                    if (element.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0)
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

                    if (element.TryGetProperty("usageMetadata", out var usageMetadata))
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
                       yield return new StreamResponse(text, promptTokens, outputTokens);
                    }
                }
            }
        }
    }
}