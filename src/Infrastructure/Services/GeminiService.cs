using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Infrastructure.Services
{
    public class GeminiService : IAiModelService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
       
        private const string BaseUrl = "https://generativelanguage.googleapis.com/";
        private const string ModelEndpoint = "v1beta/models/gemini-1.5-flash:streamGenerateContent";
        private const int DefaultMaxTokens = 1000;

        public GeminiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration
           )
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri(BaseUrl);
            _apiKey = configuration.GetValue<string>("AI:Gemini:ApiKey") ??
                      throw new ArgumentNullException(nameof(configuration), "Gemini API key not found");
           
        }

        public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
        {
            var messages = PrepareMessages(history);
            var requestBody = CreateRequestBody(messages);
            var response = await SendRequestAsync(requestBody);

            await foreach (var chunk in ProcessResponseStream(response))
            {
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    yield return chunk;
                }
            }
        }

        private static List<object> PrepareMessages(IEnumerable<MessageDto> history)
        {
            return history
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new
                {
                    role = m.IsFromAi ? "model" : "user",
                    parts = new[] { new { text = m.Content.Trim() } }
                })
                .ToList<object>();
        }

        private static object CreateRequestBody(List<object> messages)
        {
            return new
            {
                contents = messages,
                generationConfig = new { maxOutputTokens = DefaultMaxTokens }
            };
        }

        private async Task<HttpResponseMessage> SendRequestAsync(object requestBody)
        {
            var endpoint = $"{ModelEndpoint}?key={_apiKey}";
            var jsonRequest = JsonSerializer.Serialize(requestBody);
          

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

         
            return response;
        }

        private async IAsyncEnumerable<string> ProcessResponseStream(HttpResponseMessage response)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

               

                // Handle the direct JSON response without "data:" prefix
                if (line.StartsWith("[") || line.StartsWith("{"))
                {
                    var text = ExtractTextFromJson(line);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        yield return text;
                    }

                    continue;
                }

                // Handle SSE format with "data:" prefix
                if (line.StartsWith("data:"))
                {
                    var jsonContent = line["data:".Length..].Trim();
                    var text = ExtractTextFromJson(jsonContent);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        yield return text;
                    }
                }
            }
        }

        private static string ExtractTextFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Handle array response
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var textBuilder = new StringBuilder();
                    foreach (var element in root.EnumerateArray())
                    {
                        var text = ExtractTextFromElement(element);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            textBuilder.Append(text);
                        }
                    }

                    return textBuilder.ToString();
                }

                // Handle single object response
                return ExtractTextFromElement(root);
            }
            catch (JsonException ex)
            {
                // Log but don't throw - this might be a partial JSON chunk
                Console.WriteLine($"JSON parsing error: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractTextFromElement(JsonElement element)
        {
            if (!element.TryGetProperty("candidates", out var candidates))
                return string.Empty;

            var textBuilder = new StringBuilder();
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts))
                    continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String)
                    {
                        textBuilder.Append(textElement.GetString());
                    }
                }
            }

            return textBuilder.ToString();
        }
    }
}