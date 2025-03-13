using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

namespace Infrastructure.Services.Plugins
{
    public class PerplexityPlugin : IChatPlugin
    {

        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public PerplexityPlugin(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
        }

        public bool CanHandle(string userMessage)
        {
            return userMessage?.StartsWith("/pplx") ?? false;
        }

        public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                var cleanMessage = userMessage.Replace("/pplx", "").Trim();
                var resultBuilder = new StringBuilder();

                var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Headers = { { "Authorization", $"Bearer {_apiKey}" } },
                    Content = new StringContent(
                        JsonConvert.SerializeObject(new
                        {
                            model = "sonar-deep-research",
                            messages = new[] { new { role = "user", content = cleanMessage } },
                            stream = true
                        }),
                        Encoding.UTF8,
                        "application/json")
                };

                using var response = await _httpClient.SendAsync(
                    request, 
                    HttpCompletionOption.ResponseHeadersRead, 
                    cancellationToken
                );

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("data: "))
                    {
                        var json = line[6..];
                        var responseData = JsonConvert.DeserializeObject<PerplexityResponse>(json);
                        resultBuilder.Append(responseData?.Choices[0].Message.Content);
                    }
                }

                return new PluginResult(
                    resultBuilder.ToString(),
                    true
                );
            }
            catch (Exception ex)
            {
                return new PluginResult(
                    $"Perplexity AI Error: {ex.Message}",
                    false,
                    ex.Message
                );
            }
        }
    }

    // Response DTOs
    public class PerplexityResponse
    {
        public List<PerplexityChoice> Choices { get; set; }
    }

    public class PerplexityChoice
    {
        public PerplexityMessage Message { get; set; }
    }

    public class PerplexityMessage
    {
        public string Content { get; set; }
    }
}