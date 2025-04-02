using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;
using System.Net.Http.Headers;

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
                Headers = {
                    { "Authorization", $"Bearer {_apiKey}" },
                    { "Accept", "application/json" }
                },
                Content = new StringContent(
                    JsonConvert.SerializeObject(new
                    {
                        model = "sonar-pro",
                        messages = new[] {
                            new { role = "system", content = "Be precise and concise." },
                            new { role = "user", content = cleanMessage }
                        },
                        stream = true
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Perplexity API request failed with status code {response.StatusCode}. Response: {errorBody}", null, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var resultBuilder = new StringBuilder();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("data: "))
                {
                    var json = line[6..];
                    if (json.Equals("[DONE]", StringComparison.OrdinalIgnoreCase)) break;
                    
                    try
                    {
                        var responseData = JsonConvert.DeserializeObject<PerplexityResponse>(json);
                        if (responseData?.Choices != null && responseData.Choices.Count > 0 && responseData.Choices[0].Message != null)
                        {
                           resultBuilder.Append(responseData.Choices[0].Message.Content);
                        }
                    }
                    catch(JsonException jsonEx)
                    {
                       Console.WriteLine($"Error parsing Perplexity stream chunk: {jsonEx.Message}. Chunk: {json}"); 
                    }
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