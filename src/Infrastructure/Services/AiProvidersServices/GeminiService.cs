using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

public class GeminiService : BaseAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public GeminiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null, ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters) { }

    protected override void ConfigureHttpClient() { }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override List<(string Role, string Content)> PrepareMessageList(IEnumerable<MessageDto> history)
    {
        var messages = new List<(string Role, string Content)>();
        var systemMessage = GetSystemMessage();
        if (!string.IsNullOrEmpty(systemMessage))
        {
            messages.Add(("system", systemMessage));
        }
        
        if (ShouldEnableThinking())
        {
            messages.Add(("system", "When solving complex problems, please show your detailed step-by-step thinking process marked as '### Thinking:' before providing the final answer marked as '### Answer:'. Analyze all relevant aspects thoroughly."));
        }
        
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            messages.Add((msg.IsFromAi ? "assistant" : "user", msg.Content.Trim()));
        }
        
        return messages;
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history);
        var contents = messages.Select(m => new { role = m.Role == "assistant" ? "model" : m.Role == "system" ? "user" : "user", parts = new[] { new { text = m.Content } } }).ToList();

        var parameters = GetModelParameters();
        var generationConfig = new Dictionary<string, object>();

        // Define supported parameters for Gemini
        var supportedParams = new HashSet<string>() { "temperature", "topP", "topK", "maxOutputTokens" };
        
        // Map and filter parameters for Gemini
        if (parameters.ContainsKey("temperature")) generationConfig["temperature"] = parameters["temperature"];
        if (parameters.ContainsKey("top_p")) generationConfig["topP"] = parameters["top_p"];
        if (parameters.ContainsKey("top_k")) generationConfig["topK"] = parameters["top_k"];
        if (parameters.ContainsKey("max_tokens")) generationConfig["maxOutputTokens"] = parameters["max_tokens"];
        if (parameters.ContainsKey("stop")) generationConfig["stopSequences"] = parameters["stop"];

        // Remove any parameters not in the supported list
        var keysToRemove = generationConfig.Keys.Where(k => !supportedParams.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            Console.WriteLine($"Removing unsupported parameter for Gemini: {key}");
            generationConfig.Remove(key);
        }

        var requestObj = new { contents, generationConfig };
        return requestObj;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        var request = CreateRequest(requestBody);
        
        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to Gemini: {ex.Message}");
            throw;
        }
        
        // Auto-correction loop - try up to 3 times to fix parameter issues
        int maxRetries = 3;
        int retryCount = 0;
        
        while (!response.IsSuccessStatusCode && retryCount < maxRetries)
        {
            retryCount++;
            
            try
            {
                // Extract error details
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Gemini API Error (attempt {retryCount}): {errorContent}");
                
                // For Gemini, we'll simplify by removing all generation config parameters and trying again
                // This crude approach should work for most cases where parameters are causing issues
                var simpleRequestBody = new 
                { 
                    contents = ((dynamic)requestBody).contents,
                    generationConfig = new Dictionary<string, object>() 
                };
                
                Console.WriteLine($"Retrying Gemini request with minimal parameters (attempt {retryCount})");
                var retryRequest = CreateRequest(simpleRequestBody);
                response = await HttpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during auto-correction attempt {retryCount}: {ex.Message}");
                break;
            }
        }
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Gemini");
            yield break;
        }

        var tokenCount = 0;
        var fullResponse = new StringBuilder();
        
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
       
            var responseArray = await JsonSerializer.DeserializeAsync<JsonElement?>(stream, cancellationToken: cancellationToken);
            if (responseArray.HasValue)
            {
                foreach (var root in responseArray.Value.EnumerateArray())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    string? text = null;
                    if (root.TryGetProperty("candidates", out var candidates) && 
                        candidates[0].TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) && 
                        parts[0].TryGetProperty("text", out var textElement))
                    {
                        text = textElement.GetString();
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        fullResponse.Append(text);
                        tokenCount = EstimateGeminiTokens(fullResponse.ToString());
                        yield return new StreamResponse(text, tokenCount, fullResponse.Length);
                    }
                }
            }
       
    }
    
    private int EstimateGeminiTokens(string text)
    {
        // Simple approximation: ~4 characters per token for English text
        return text.Length / 4;
    }
}