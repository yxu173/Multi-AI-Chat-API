using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services;

public class Imagen3Service : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _projectId;
    private readonly string _region;
    private readonly string _publisher;
    private readonly string _modelId;

    public Imagen3Service(
        IHttpClientFactory httpClientFactory, 
        string apiKey,
        string projectId,
        string region,
        string publisher,
        string modelId)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://us-central1-aiplatform.googleapis.com/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _apiKey = apiKey;
        _projectId = projectId;
        _region = region;
        _publisher = publisher;
        _modelId = modelId;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history,Action<int, int>? tokenCallback = null)
    {
        var latestUserMessage = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) && !m.IsFromAi)
            .LastOrDefault();

        if (latestUserMessage == null)
        {
            throw new ArgumentException("No valid user message found in conversation history");
        }

        var prompt = latestUserMessage.Content;
        
        var requestBody = new
        {
            instances = new[]
            {
                new
                {
                    prompt,
                    num_images = 2,
                    size = "1024x1024" 
                }
            }
        };

        var endpoint = $"v1/projects/{_projectId}/locations/{_region}/publishers/{_publisher}/models/{_modelId}:predict";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(jsonResponse);
        var root = document.RootElement;

        if (root.TryGetProperty("predictions", out var predictions) && predictions.ValueKind == JsonValueKind.Array)
        {
            for (int i = 0; i < predictions.GetArrayLength(); i++)
            {
                if (predictions[i].TryGetProperty("bytesBase64Encoded", out var imageData))
                {
                    var imageUrl = $"data:image/png;base64,{imageData.GetString()}";
                    yield return $"![Generated Image {i+1}]({imageUrl})";
                }
            }
        }
        else
        {
            yield return "Sorry, I couldn't generate any images.";
        }
    }
}