using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;

public class PerplexityPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public string Name => "Perplexity AI";
    public string Description => "Advanced research assistant using the Perplexity API";

    public PerplexityPlugin(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    public bool CanHandle(string userMessage)
    {
        return userMessage.StartsWith("/pplx", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var cleanMessage = userMessage.Replace("/pplx", "").Trim();
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

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var resultBuilder = new StringBuilder();
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
            return new PluginResult(resultBuilder.ToString(), true);
        }
        catch (Exception ex)
        {
            return new PluginResult("", false, $"Perplexity request failed: {ex.Message}");
        }
    }
}

public class PerplexityResponse
{
    public List<Choice> Choices { get; set; } = new List<Choice>();
}

public class Choice
{
    public Messages Message { get; set; }
}

public class Messages
{
    public string Content { get; set; }
}