using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Application.Services;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

public class GeminiService : IAiModelService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
        _apiKey = configuration["AI:Gemini:ApiKey"] ?? throw new ArgumentNullException("API key is missing");
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var validHistory = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(2)
            .ToList();

        if (!validHistory.Any())
        {
            throw new ArgumentException("No valid messages in conversation history");
        }

        var contents = validHistory.Select(m => new
        {
            role = m.IsFromAi ? "model" : "user",
            parts = new[] { new { text = m.Content } }
        }).ToArray();

        var requestBody = new
        {
            contents,
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 2048,
                topP = 0.8,
                topK = 40
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"v1beta/models/gemini-2.0-flash-exp:streamGenerateContent?key={_apiKey}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Raw response: {jsonResponse}");

      
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0 &&
                            parts[0].TryGetProperty("text", out var textElement))
                        {
                            var text = textElement.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                yield return text;
                            }
                        }
                    }
                }
            }
            else if (root.TryGetProperty("candidates", out var candidates) &&
                     candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }
            }
            else
            {
                Console.WriteLine("Unexpected response format");
                yield return "No valid response from the API.";
            }
       
    }
}

// JSON response structure classes
public class Response
{
    public Candidate[] Candidates { get; set; }
}

public class Candidate
{
    public Content Content { get; set; }
}

public class Content
{
    public Part[] Parts { get; set; }
}

public class Part
{
    public string Text { get; set; }
}