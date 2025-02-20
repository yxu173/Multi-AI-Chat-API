using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Application.Abstractions.Interfaces;

namespace Application.Services
{
    public class DeepSeekService : IAiModelService
    {
        private const string DEEPSEEK_MODEL = "deepseek-chat";
        private readonly HttpClient _httpClient;
        private readonly string _deepseekApiKey;
        private readonly ILogger<DeepSeekService> _logger;

        public DeepSeekService(IConfiguration configuration, HttpClient httpClient, ILogger<DeepSeekService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _deepseekApiKey = configuration["AI:DeepSeek:ApiKey"]
                              ?? throw new ArgumentNullException("API key is missing");

            _httpClient.BaseAddress = new Uri("https://api.deepseek.com");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_deepseekApiKey}");
        }

        public async Task<string> GetResponseAsync(string modelType, string message)
        {
            var requestBody = new
            {
                model = DEEPSEEK_MODEL,
                messages = new[] { new { role = "user", content = message } },
                max_tokens = 1024,
                temperature = 1.5
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending request to DeepSeek API with model: {Model}", DEEPSEEK_MODEL);

            try
            {
                var response = await _httpClient.PostAsync("/chat/completions", content);

                _logger.LogInformation("DeepSeek API Response Status Code: {StatusCode}", response.StatusCode);
                _logger.LogInformation("DeepSeek API Response Headers: {Headers}", response.Headers.ToString());
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("DeepSeek API Response Content: {Content}", responseContent);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogError("DeepSeek API Rate Limited: {Content}", responseContent);
                    return string.Empty;
                }
                else if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("DeepSeek API Error: Status Code {StatusCode}, Content: {Content}",
                        response.StatusCode, responseContent);
                    return string.Empty;
                }

                var result = JsonSerializer.Deserialize<DeepSeekResponse>(responseContent);
                if (result == null || result.Choices == null || result.Choices.Length == 0 ||
                    string.IsNullOrEmpty(result.Choices[0].Message.Content))
                {
                    _logger.LogError("Empty or malformed response from DeepSeek API: {Response}", responseContent);
                    return string.Empty;
                }

                return result.Choices[0].Message.Content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetResponseAsync");
                throw;
            }
        }

        public async IAsyncEnumerable<string> GetStreamingResponseAsync(string modelType, string message)
        {
            var requestBody = new
            {
                model = DEEPSEEK_MODEL,
                messages = new[] { new { role = "user", content = message } },
                max_tokens = 1024,
                stream = true
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending streaming request to DeepSeek API with model: {Model}", DEEPSEEK_MODEL);
            _logger.LogDebug("Request body: {Body}", jsonContent);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
            {
                Content = content
            };
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            _logger.LogInformation("Response Status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("DeepSeek API Error Response: {Error}", errorContent);
                response.EnsureSuccessStatusCode();
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("data: "))
                {
                    var dataLine = line["data: ".Length..].Trim();
                    if (dataLine == "[DONE]")
                        break;

                    _logger.LogDebug("Received line: {Line}", dataLine);

                    var chunk = JsonSerializer.Deserialize<DeepSeekStreamResponse>(dataLine, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (chunk?.Type == "message_start" || chunk?.Type == "content_block_start")
                    {
                        _logger.LogDebug("Control message received: {Type}", chunk?.Type);
                        continue;
                    }

                    if (chunk?.Type == "content_block_delta" && !string.IsNullOrEmpty(chunk.Delta?.Text))
                    {
                        _logger.LogDebug("Content block delta received: {Text}", chunk.Delta.Text);
                        yield return chunk.Delta.Text;
                    }
                }
            }
        }

        // Data models for JSON deserialization.
        private class DeepSeekResponse
        {
            public string Id { get; set; }
            public string Object { get; set; }
            public int Created { get; set; }
            public string Model { get; set; }
            public Choice[] Choices { get; set; }
            public Usage Usage { get; set; }
        }

        private class Choice
        {
            public int Index { get; set; }
            public Message Message { get; set; }
            public object Logprobs { get; set; }
            public string FinishReason { get; set; }
        }

        private class Message
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }

        private class Usage
        {
            public int PromptTokens { get; set; }
            public int CompletionTokens { get; set; }
            public int TotalTokens { get; set; }
        }

        private class DeepSeekStreamResponse
        {
            public string Type { get; set; }
            public MessageDelta Delta { get; set; }
            public int Index { get; set; }

            public class MessageDelta
            {
                public string Type { get; set; }
                public string Text { get; set; }
            }
        }
    }
}