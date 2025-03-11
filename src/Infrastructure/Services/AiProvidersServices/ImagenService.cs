using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Services;
using Google.Apis.Auth.OAuth2;

namespace Infrastructure.Services.AiProvidersServices;

public class ImagenService 
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _projectId;
    private readonly string _region;
    private readonly string _publisher;
    private readonly string _modelId;
    private readonly string _imageSavePath = "wwwroot/images";

    public ImagenService(
        IHttpClientFactory httpClientFactory,
        string projectId,
        string region,
        string publisher,
        string modelId)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://us-central1-aiplatform.googleapis.com/");
        _projectId = projectId;
        _region = region;
        _publisher = publisher;
        _modelId = modelId;
        Directory.CreateDirectory(_imageSavePath);
        var credential = GoogleCredential.GetApplicationDefault();
        if (credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
        }

        var token = credential.UnderlyingCredential.GetAccessTokenForRequestAsync().Result;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history,
        Action<int, int>? tokenCallback = null)
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

        var endpoint =
            $"v1/projects/{_projectId}/locations/{_region}/publishers/{_publisher}/models/{_modelId}:predict";
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
            int imageCount = 0;
            foreach (var prediction in predictions.EnumerateArray())
            {
                if (imageCount >= 2)
                    break;

                if (prediction.TryGetProperty("bytesBase64Encoded", out var base64Element) &&
                    prediction.TryGetProperty("mimeType", out var mimeTypeElement))
                {
                    var base64Image = base64Element.GetString();
                    var mimeType = mimeTypeElement.GetString();

                    if (!string.IsNullOrEmpty(base64Image))
                    {
                        var imageBytes = Convert.FromBase64String(base64Image);

                        var extension = mimeType.Split('/').Last();

                        var imageUrl = SaveImageLocally(imageBytes, $"{Guid.NewGuid()}.{extension}");
                        yield return $"![generated image]({imageUrl})";
                        imageCount++;
                    }
                }
            }
        }
        else
        {
            yield return "Error: No valid predictions found in the response.";
        }
    }

    private string SaveImageLocally(byte[] imageBytes, string fileName)
    {
        var filePath = Path.Combine(_imageSavePath, fileName);
        File.WriteAllBytes(filePath, imageBytes);
        return $"/images/{fileName}";
    }
}