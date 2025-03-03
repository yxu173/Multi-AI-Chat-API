using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
    private readonly TokenCountingService _tokenCountingService;

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
        _tokenCountingService = new TokenCountingService();
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
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
    
    public async Task<TokenUsage> CountTokensAsync(IEnumerable<MessageDto> messages)
    {
        // For image generation, we'll use a simplified token counting approach
        // since it's not directly comparable to text tokens
        var latestUserMessage = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) && !m.IsFromAi)
            .LastOrDefault();
            
        int inputTokens = latestUserMessage != null 
            ? _tokenCountingService.EstimateTokenCount(latestUserMessage.Content) 
            : 0;
            
        // Image generation typically has a fixed cost per image
        // We'll estimate this as equivalent to 1000 output tokens
        int outputTokens = 1000;
        
        var totalCost = _tokenCountingService.CalculateCost(
            inputTokens,
            outputTokens,
            0.001, // Placeholder values - should come from the model
            0.002);
            
        return new TokenUsage(inputTokens, outputTokens, totalCost);
    }
}