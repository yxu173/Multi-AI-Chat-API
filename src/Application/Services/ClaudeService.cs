using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Application.Abstractions.Interfaces;

namespace Application.Services
{
    public class ClaudeService : IAiModelService
    {
        private const string CLAUDE_MODEL = "claude-3-sonnet-20240229";
        private readonly HttpClient _httpClient;
        private readonly string _claudeApiKey;
        private readonly ILogger<ClaudeService> _logger;

        public ClaudeService(IConfiguration configuration, HttpClient httpClient, ILogger<ClaudeService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _claudeApiKey = configuration["AI:Claude:ApiKey"] ?? throw new ArgumentNullException("API key is missing");

            // Configure HttpClient for Anthropic's API.
            _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _claudeApiKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_claudeApiKey}");
        }

        public async Task<string> GetResponseAsync(string modelType, string message)
        {
            var requestBody = new
            {
                model = CLAUDE_MODEL,
                messages = new[] { new { role = "user", content = message } },
                max_tokens = 1024,
                temperature = 0.7
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending request to Claude API with model: {Model}", CLAUDE_MODEL);

            try
            {
                var response = await _httpClient.PostAsync("messages", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Received response from Claude API: {Response}", responseContent);

                var result = JsonSerializer.Deserialize<ClaudeResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null || result.Content == null || result.Content.Length == 0)
                {
                    _logger.LogError("Empty response content from Claude API");
                    return string.Empty;
                }

                return result.Content[0].Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetResponseAsync for model {Model}", CLAUDE_MODEL);
                throw;
            }
        }

        /// <summary>
        /// Streams a response from the Claude API.
        /// </summary>
        /// <param name="modelType">Model type (currently unused as the model is fixed).</param>
        /// <param name="message">User message to send.</param>
        public async IAsyncEnumerable<string> GetStreamingResponseAsync(string modelType, string message)
        {
            var requestBody = new
            {
                model = CLAUDE_MODEL,
                messages = new[] { new { role = "user", content = message } },
                max_tokens = 1024,
                stream = true
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending streaming request to Claude API with model: {Model}", CLAUDE_MODEL);
            _logger.LogDebug("Request body: {Body}", jsonContent);

            using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = content
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                _logger.LogInformation("Response Status: {StatusCode}", response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Claude API Error Response: {Error}", errorContent);
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

                        var chunk = JsonSerializer.Deserialize<ClaudeStreamResponse>(dataLine, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        // Ignore control messages.
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
        private class ClaudeResponse
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string Role { get; set; }
            public ContentBlock[] Content { get; set; }
            public string Model { get; set; }
            public string StopReason { get; set; }
            public Usage Usage { get; set; }
        }

        private class ContentBlock
        {
            public string Type { get; set; }
            public string Text { get; set; }
        }

        private class Usage
        {
            public int InputTokens { get; set; }
            public int OutputTokens { get; set; }
        }

        private class ClaudeStreamResponse
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
