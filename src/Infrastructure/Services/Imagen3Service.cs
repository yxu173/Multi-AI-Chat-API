using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services
{
    public class Imagen3Service : IAiModelService
    {
        private readonly HttpClient _httpClient;
        private readonly string _projectId;
        private readonly string _region;
        private readonly string _modelId;
        private readonly string _imageSavePath = "wwwroot/images";

        // Constructor with dependency injection
        public Imagen3Service(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _region = configuration["AI:Imagen3:Region"] ?? throw new ArgumentNullException("Region is missing");
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri($"https://{_region}-aiplatform.googleapis.com/");
            _projectId = configuration["AI:Imagen3:ProjectId"] ??
                         throw new ArgumentNullException("Project ID is missing");
            _modelId = configuration["AI:Imagen3:ModelId"] ?? throw new ArgumentNullException("Model ID is missing");
            Directory.CreateDirectory(_imageSavePath); // Ensure the image save directory exists
            var credential = GoogleCredential.GetApplicationDefault();
            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
            }

            var token = credential.UnderlyingCredential.GetAccessTokenForRequestAsync().Result;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Implementation of the IAiModelService method
        public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
        {
            // Filter and take the last 2 valid messages from history
            var validHistory = history
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .TakeLast(2)
                .ToList();

            // Validate the history
            if (!validHistory.Any())
            {
                throw new ArgumentException("No valid messages in conversation history");
            }

            // Combine the messages into a single prompt
            var prompt = validHistory.Select(m => m.Content).Aggregate((a, b) => $"{a}\n{b}");

            // Construct the request body for Vertex AI predict endpoint
            var requestBody = new
            {
                instances = new[]
                {
                    new
                    {
                        prompt,
                        num_images = 2, // Number of images to generate
                        size = "1024x1024" 
                    }
                }
            };

            // Set up the HTTP request
            var endpoint = $"v1/projects/{_projectId}/locations/{_region}/publishers/google/models/{_modelId}:predict";
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            // Send the request and get the response
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // Parse the JSON response
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;

            // Extract predictions from the response
            if (root.TryGetProperty("predictions", out var predictions) && predictions.ValueKind == JsonValueKind.Array)
            {
                foreach (var prediction in predictions.EnumerateArray())
                {
                    // Extract base64-encoded image data and MIME type
                    if (prediction.TryGetProperty("bytesBase64Encoded", out var base64Element) &&
                        prediction.TryGetProperty("mimeType", out var mimeTypeElement))
                    {
                        var base64Image = base64Element.GetString();
                        var mimeType = mimeTypeElement.GetString();

                        if (!string.IsNullOrEmpty(base64Image))
                        {
                            // Decode base64 to bytes
                            var imageBytes = Convert.FromBase64String(base64Image);
                            // Determine file extension from MIME type
                            var extension = mimeType.Split('/').Last();
                            // Save the image and yield its URL
                            var imageUrl = SaveImageLocally(imageBytes, $"{Guid.NewGuid()}.{extension}");
                            yield return $"![generated image]({imageUrl})";
                        }
                    }
                }
            }
            else
            {
                yield return "Error: No valid predictions found in the response.";
            }
        }

        // Helper method to save the image locally and return its URL
        private string SaveImageLocally(byte[] imageBytes, string fileName)
        {
            var filePath = Path.Combine(_imageSavePath, fileName);
            File.WriteAllBytes(filePath, imageBytes);
            return $"/images/{fileName}"; // Relative URL to the saved image
        }
    }
}