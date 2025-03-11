using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services.AiProvidersServices
{
    public class GeminiService : IAiModelService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _modelCode;

        public GeminiService(
            IHttpClientFactory httpClientFactory,
            string apiKey,
            string modelCode)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            _apiKey = apiKey;
            _modelCode = modelCode;
        }

        public async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
            Action<int, int>? tokenCallback = null)
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

            var requestBody = new
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

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"v1beta/models/{_modelCode}:streamGenerateContent?key={_apiKey}")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

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

                    tokenCallback?.Invoke(promptTokens, outputTokens);

                    if (!string.IsNullOrEmpty(text))
                    {
                       yield return new StreamResponse(text, promptTokens, outputTokens);
                    }
                }
            }
        }

        public class Response
        {
            public Candidate[] Candidates { get; set; }
        }

        public class Candidate
        {
            public Content Content { get; set; }
        }

        public class Content
        {
            public Part[] Parts { get; set; }
        }

        public class Part
        {
            public string Text { get; set; }
        }
    }
}