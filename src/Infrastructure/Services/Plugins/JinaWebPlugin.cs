using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text.Json.Nodes; // For JsonObject
using System.Text.Json; // For JsonValue, JsonValueKind

namespace Infrastructure.Services.Plugins;

public class JinaWebPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _maxRetries;

    // Tool name for AI
    public string Name => "read_webpage";
    public string Description => "Retrieve and summarize web content from a specific URL using Jina AI Reader.";

    public JinaWebPlugin(HttpClient httpClient, string apiKey, int maxRetries = 3)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = string.IsNullOrEmpty(apiKey) ? throw new ArgumentNullException(nameof(apiKey), "Jina API Key cannot be empty") : apiKey;
        _maxRetries = maxRetries;
    }

    public JsonObject GetParametersSchema()
    {
        string schemaJson = """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "format": "uri",
              "description": "The full URL (including http:// or https://) of the webpage to read."
            }
          },
          "required": ["url"]
        }
        """;
        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    public async Task<PluginResult> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
      
        if (arguments == null || !arguments.TryGetPropertyValue("url", out var urlNode) || urlNode is not JsonValue urlValue || urlValue.GetValueKind() != JsonValueKind.String)
        {
            return new PluginResult("", false, "Missing or invalid 'url' argument for Read Webpage.");
        }

        string url = urlValue.GetValue<string>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult) || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
             return new PluginResult("", false, "'url' argument must be a valid absolute HTTP or HTTPS URL.");
        }

        HttpResponseMessage? response = null;
        try
        {
            const string jinaRequestUrl = "https://r.jina.ai/"; // Use reader endpoint

            int retries = 0;
            bool success = false;

            while (retries < _maxRetries && !success && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{jinaRequestUrl}{url}");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    if (!string.IsNullOrEmpty(_apiKey))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    }

                    response?.Dispose();
                    response = await _httpClient.SendAsync(request, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        success = true;
                    }
                    else if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                         retries++;
                         if (retries < _maxRetries) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), cancellationToken);
                    }
                    else
                    {
                         break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Jina Web request cancelled during retry loop.");
                    return new PluginResult("", false, "Jina Web request cancelled.");
                }
                catch (Exception ex) when (retries < _maxRetries - 1)
                {
                    Console.WriteLine($"Jina request error (Attempt {retries + 1}/{_maxRetries}): {ex.Message}");
                    retries++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), cancellationToken);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Jina Web request cancelled after retry loop.");
                return new PluginResult("", false, "Jina Web request cancelled.");
            }

            if (!success || response == null)
            {
                var errorDetail = response != null ? $"Status code: {response.StatusCode}" : "No response received.";
                string errorBody = "";
                 if (response?.Content != null) {
                     try { errorBody = " Body: " + await response.Content.ReadAsStringAsync(CancellationToken.None); }
                     catch { errorBody = " (Could not read error body)"; }
                 }
                Console.WriteLine($"Jina request failed after {retries} retries. {errorDetail}{errorBody}");
                return new PluginResult("", false, $"Failed to retrieve content from Jina after {retries} attempt(s). {errorDetail}");
            }

            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(contentString))
            {
                 return new PluginResult("", false, $"Jina AI returned empty content for the URL.");
            }

            var formattedResult = $"Content from {url}:\n\n{contentString.Trim()}";
            return new PluginResult(formattedResult, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Jina Web request failed unexpectedly: {ex}");
            return new PluginResult("", false, $"Jina Web request failed unexpectedly: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            response?.Dispose();
        }
    }
}
