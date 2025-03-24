using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultThinkingBudget = 16000;

    public AnthropicService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null, ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters) { }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
    }

    protected override string GetEndpointPath() => "messages";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history);
        var systemMessage = messages.FirstOrDefault(m => m.Role == "system").Content;
        var otherMessages = messages.Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content }).ToList();
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);
        
        // Remove parameters not supported by Anthropic
        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
        
        if (ShouldEnableThinking())
        {
            requestObj.Remove("top_k");
            requestObj.Remove("top_p");
        }
        
        if (!requestObj.ContainsKey("max_tokens"))
        {
            requestObj["max_tokens"] = 20000;
        }
        
        if (!string.IsNullOrEmpty(systemMessage)) requestObj["system"] = systemMessage;
        requestObj["messages"] = otherMessages;
        return requestObj;
    }

    protected override void AddProviderSpecificParameters(Dictionary<string, object> requestObj)
    {
        if (ShouldEnableThinking())
        {
            requestObj["temperature"] = 1.0;
            requestObj["thinking"] = new { type = "enabled", budget_tokens = DefaultThinkingBudget };
        }
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
            Console.WriteLine($"Error sending request to Anthropic: {ex.Message}");
            throw;
        }
        
        // Auto-correction loop - try up to 3 times to fix parameter issues
        int maxRetries = 3;
        int retryCount = 0;
        Dictionary<string, object>? currentRequestBody = requestBody as Dictionary<string, object>;
        
        while (!response.IsSuccessStatusCode && retryCount < maxRetries && currentRequestBody != null)
        {
            retryCount++;
            
            try
            {
                // Extract error details for possible auto-correction
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                string errorType = "unknown";
                string errorParam = "none";
                
                try
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(errorContent);
                    if (errorJson.TryGetProperty("error", out var errorObj))
                    {
                        errorType = errorObj.TryGetProperty("type", out var type) ? type.GetString() ?? "unknown" : "unknown";
                        errorParam = errorObj.TryGetProperty("param", out var param) ? param.GetString() ?? "none" : "none";
                    }
                }
                catch
                {
                    // If we can't parse error details, continue with default values
                }
                
                // Attempt auto-correction if applicable
                var (correctionSuccess, retryResponse, correctedBody) = 
                    await AttemptAutoCorrection(response, currentRequestBody, errorType, errorParam, "Anthropic");
                
                if (correctionSuccess && retryResponse != null && correctedBody != null)
                {
                    Console.WriteLine($"Auto-correction attempt {retryCount} successful, continuing with corrected request");
                    response = retryResponse;
                    currentRequestBody = correctedBody;
                }
                else
                {
                    // If auto-correction failed, give up and handle the original error
                    Console.WriteLine($"Auto-correction attempt {retryCount} failed, giving up");
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during auto-correction attempt {retryCount}: {ex.Message}");
                break; // Exit the retry loop on exception
            }
        }
        
        // If we still have an error after retries, handle it
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Anthropic");
            yield break;
        }

        var fullResponse = new StringBuilder();
        int tokenCount = 0;
        
        await foreach (var json in ReadStreamAsync(response, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var type = doc.RootElement.GetProperty("type").GetString();
                if (type == "content_block_delta" && doc.RootElement.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("text", out var textElement))
                    {
                        text = textElement.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error processing Anthropic response chunk: {ex.Message}");
            }
            
            if (!string.IsNullOrEmpty(text))
            {
                fullResponse.Append(text);
                tokenCount = EstimateAnthropicTokens(fullResponse.ToString());
                yield return new StreamResponse(text, tokenCount, fullResponse.Length);
            }
        }
    }
    
    private int EstimateAnthropicTokens(string text)
    {
        // Claude uses approximately 4.5 characters per token for English text
        return (int)(text.Length / 4.5);
    }
}