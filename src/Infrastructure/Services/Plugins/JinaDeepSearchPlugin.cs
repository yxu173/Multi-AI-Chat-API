using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;

namespace Infrastructure.Services.Plugins;

public class JinaDeepSearchPlugin : IChatPlugin<string>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JinaDeepSearchPlugin> _logger;
    private readonly string _apiKey;

    public string Name => "jina_deepsearch";
    public string Description => "Search the web with Jina's DeepSearch for real-time, comprehensive information.";

    public JinaDeepSearchPlugin(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        ILogger<JinaDeepSearchPlugin> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
            _apiKey = string.IsNullOrEmpty(apiKey) ? throw new ArgumentNullException(nameof(apiKey), "Jina API Key cannot be empty") : apiKey;
    }

    public JsonObject GetParametersSchema()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The search query to execute with Jina DeepSearch."
                }
            },
            ["required"] = JsonSerializer.SerializeToNode(new[] { "query" })?.AsArray()
        };
        
        return schema;
    }

    public async Task<PluginResult<string>> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (arguments == null)
            {
                return new PluginResult<string>("", false, "Arguments cannot be null");
            }

            if (!arguments.TryGetPropertyValue("query", out var queryNode) || string.IsNullOrWhiteSpace(queryNode?.GetValue<string>()))
            {
                return new PluginResult<string>("", false, "Query parameter is required");
            }

            string userQuery = queryNode.GetValue<string>();
            _logger.LogInformation("Executing Jina DeepSearch with query: {Query}", userQuery);

            // Use the streaming method to accumulate the result
            var sb = new System.Text.StringBuilder();
            await foreach (var chunk in StreamDeepSearchAsync(userQuery, cancellationToken))
            {
                sb.Append(chunk);
            }
            var result = sb.ToString();
            return new PluginResult<string>(result, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Jina DeepSearch plugin");
            return new PluginResult<string>("", false, $"Error: {ex.Message}");
        }
    }

    // Add a streaming method to yield each chunk as it arrives
    public async IAsyncEnumerable<string> StreamDeepSearchAsync(string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default, Func<string, Task>? onChunk = null)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        var messages = new List<JsonObject>
        {
            CreateMessage("user", query)
        };

        var payload = new
        {
            model = "jina-deepsearch-v1",
            messages = messages,
            stream = true,
            reasoning_effort = "high",
            max_attempts = 3,
            no_direct_answer = false,
            add_references = true,
            max_tokens = 1500
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://deepsearch.jina.ai/v1/chat/completions")
        {
            Content = content
        };

        using var streamedResponse = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!streamedResponse.IsSuccessStatusCode)
        {
            var errorContent = await streamedResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Jina DeepSearch API error: {StatusCode}, {Error}", streamedResponse.StatusCode, errorContent);
            throw new Exception($"API error: {streamedResponse.StatusCode}, {errorContent}");
        }

        using var stream = await streamedResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                line = line.Substring("data: ".Length);
                if (line == "[DONE]")
                {
                    yield break;
                }
            }

            string? text = null;
            try
            {
                var chunk = JsonSerializer.Deserialize<JsonNode>(line);
                if (chunk?["choices"]?[0]?["delta"]?["content"] is JsonValue contentValue)
                {
                    text = contentValue.GetValue<string>();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error parsing streamed response line: {Line}", line);
            }
            if (!string.IsNullOrEmpty(text))
            {
                if (onChunk != null)
                {
                    await onChunk(text);
                }
                yield return text;
            }
        }
    }

    private JsonObject CreateMessage(string role, string content)
    {
        var message = new JsonObject
        {
            ["role"] = role,
            ["content"] = content
        };
        return message;
    }

    // Streams chunks and invokes a notification callback for each chunk, returns the full result
    public async Task<string> StreamWithNotificationAsync(string query, Guid chatSessionId, Func<string, Guid, Task> notifyChunk, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in StreamDeepSearchAsync(query, cancellationToken))
        {
            sb.Append(chunk);
            if (notifyChunk != null)
            {
                await notifyChunk(chunk, chatSessionId);
            }
        }
        return sb.ToString();
    }
}
