using System.Text;
using Application.Abstractions.Interfaces;
using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace Infrastructure.Services.Plugins;

public class JinaWebPlugin : IChatPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _maxRetries;

    public string Name => "jina_web_reader";
    public string Description => "Retrieve web content from URLs using Jina AI";

    public JinaWebPlugin(HttpClient httpClient, string apiKey, int maxRetries = 3)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _maxRetries = maxRetries;
    }

    public bool CanHandle(string userMessage)
    {
        return userMessage.StartsWith("/jina", StringComparison.OrdinalIgnoreCase) ||
               userMessage.StartsWith("/web", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = null;
        try
        {
            var urlMatch = System.Text.RegularExpressions.Regex.Match(userMessage, @"(https?://\S+)");
            if (!urlMatch.Success)
            {
                return new PluginResult("", false, "No valid URL found in the message. Please include a URL starting with http:// or https://");
            }

            string url = urlMatch.Groups[1].Value;

            const string jinaRequestUrl = "https://r.jina.ai/";

            int retries = 0;
            bool success = false;

            while (retries < _maxRetries && !success && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, jinaRequestUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    
                    var payload = new { url = url };
                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    response?.Dispose();
                    response = await _httpClient.SendAsync(request, cancellationToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                         success = true;
                    }
                    else if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) 
                    { 
                         retries++;
                         await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), cancellationToken); 
                    }
                    else
                    {
                         break; 
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
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
                return new PluginResult("", false, "Jina Web request cancelled.");
            }

            if (!success || response == null)
            {
                var errorDetail = response != null ? $"Status code: {response.StatusCode}" : "No response received.";
                if (response != null) {
                     try { errorDetail += " Body: " + await response.Content.ReadAsStringAsync(CancellationToken.None); }
                     catch { errorDetail += " (Could not read error body)"; }
                }
                return new PluginResult("", false, $"Failed to retrieve content from Jina after {retries + 1} attempt(s). {errorDetail}");
            }
            
            response.EnsureSuccessStatusCode(); 
            
            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);
            
            var jinaResponse = JsonConvert.DeserializeObject<JinaApiResponse>(contentString);
            
            if (jinaResponse == null || jinaResponse.Data == null)
            {
                return new PluginResult("", false, $"Failed to parse response from Jina AI or response data is missing. Raw: {contentString}");
            }
            
            var result = FormatJinaResult(jinaResponse.Data, url);
            return new PluginResult(result, true);
        }
        catch (Exception ex)
        {
            return new PluginResult("", false, $"Jina Web request failed unexpectedly: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
             response?.Dispose();
        }
    }
    
    private string FormatJinaResult(JinaData responseData, string originalUrl)
    {
        var result = new StringBuilder();
        
        result.AppendLine($"## {responseData.Title ?? "No Title Provided"}");
        result.AppendLine($"**Source:** {responseData.Url ?? originalUrl}");
        
        if (!string.IsNullOrEmpty(responseData.Content))
        {
             result.AppendLine();
             result.AppendLine("### Content:");
             result.AppendLine(responseData.Content);
        }
        else
        {
             result.AppendLine("\n*No content returned by Jina.*");
        }

        if (!string.IsNullOrEmpty(responseData.Warning))
        {
            result.AppendLine();
            result.AppendLine($"**Warning:** {responseData.Warning}");
        }
        
        return result.ToString();
    }
}

public class JinaApiResponse
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("status")]
    public int Status { get; set; }

    [JsonProperty("data")]
    public JinaData? Data { get; set; }
}

public class JinaData
{
    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("content")]
    public string? Content { get; set; }

    [JsonProperty("warning")]
    public string? Warning { get; set; }
}
