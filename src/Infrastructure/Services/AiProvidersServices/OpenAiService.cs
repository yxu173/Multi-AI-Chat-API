using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

public class OpenAiService : BaseAiService
{
    private const string BaseUrl = "https://api.openai.com/v1/";
    private readonly IResilienceService _resilienceService;

    public OpenAiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        IResilienceService resilienceService,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
        _resilienceService = resilienceService;
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }

    protected override string GetEndpointPath() => "chat/completions";

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
            var reasoningEffort = GetReasoningEffort();
            var thinkingPrompt = GetThinkingPromptForEffort(reasoningEffort);
            messages.Add(("system", thinkingPrompt));
        }
        
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            messages.Add((msg.IsFromAi ? "assistant" : "user", msg.Content.Trim()));
        }
        
        return messages;
    }

    private string GetThinkingPromptForEffort(string reasoningEffort)
    {
        return reasoningEffort switch
        {
            "low" => "For simpler questions, provide a brief explanation under '### Thinking:' then give your answer under '### Answer:'. Keep your thinking concise.",
            
            "high" => "When solving problems, use thorough step-by-step reasoning:\n" +
                      "1. First, under '### Thinking:', carefully explore the problem from multiple perspectives\n" +
                      "2. Analyze all relevant factors and potential approaches\n" +
                      "3. Consider edge cases and alternative solutions\n" +
                      "4. Then provide your comprehensive final answer under '### Answer:'\n" +
                      "Make your thinking process explicit, detailed, and logically structured.",
                      
            _ => "When solving problems, use step-by-step reasoning:\n" +
                 "1. First, walk through your thought process under '### Thinking:'\n" +
                 "2. Then provide your final answer under '### Answer:'\n" +
                 "Make your thinking process clear and logical."
        };
    }

    private string GetReasoningEffort()
    {
        // Get reasoning effort from model parameters
        if (CustomModelParameters?.ReasoningEffort.HasValue == true)
        {
            return CustomModelParameters.ReasoningEffort.Value switch
            {
                <= 33 => "low",
                >= 66 => "high",
                _ => "medium"
            };
        }
        
        
        // Default to medium
        return "medium";
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history).Select(m => new { role = m.Role, content = m.Content }).ToList();
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);
        
        // Identify specific model type
        bool isGpt4o = ModelCode.Contains("gpt-4o");
        bool isGpt4 = ModelCode.Contains("gpt-4") && !isGpt4o;
        bool isGpt35 = ModelCode.Contains("gpt-3.5");
        bool isClaude = ModelCode.Contains("claude");
        bool isO3 = ModelCode.Contains("o3");
        
        // Special parameters to remove for certain models
        var parametersToRemove = new List<string>();
        
        // Handle special models like Claude and similar
        if (isClaude || isO3)
        {
            // Claude and similar models don't support most standard parameters
            parametersToRemove.AddRange(new[] {
                "temperature", "top_p", "top_k", "frequency_penalty", 
                "presence_penalty", "reasoning_effort", "seed", "response_format"
            });
        }
        // For older GPT models, remove reasoning_effort which is only for GPT-4o
        else if (isGpt4 || isGpt35)
        {
            parametersToRemove.Add("reasoning_effort");
        }
        
        // For all OpenAI models, remove top_k which is not supported
        parametersToRemove.Add("top_k");
        
        // Remove identified unsupported parameters
        foreach (var param in parametersToRemove)
        {
            if (requestObj.ContainsKey(param))
            {
                requestObj.Remove(param);
                Console.WriteLine($"Preemptively removed {param} parameter for model {ModelCode}");
            }
        }
        
        // Handle max_tokens conversion for all OpenAI models
        if (requestObj.ContainsKey("max_tokens"))
        {
            var maxTokensValue = requestObj["max_tokens"];
            requestObj.Remove("max_tokens");
            requestObj["max_completion_tokens"] = maxTokensValue;
        }
        
        requestObj["messages"] = messages;
        return requestObj;
    }

    protected override void AddProviderSpecificParameters(Dictionary<string, object> requestObj)
    {
        if (ShouldEnableThinking())
        {
            string reasoningEffort = GetReasoningEffort();
            
            // Only GPT-4o actually supports reasoning_effort parameter
            if (ModelCode.Contains("gpt-4o")) 
            {
                requestObj["reasoning_effort"] = reasoningEffort;
                
                // Ensure we have a text response format
                requestObj["response_format"] = new { type = "text" };
                
                // Add seed for consistency if not already set
                if (!requestObj.ContainsKey("seed"))
                {
                    requestObj["seed"] = 42;
                }
            }
        }
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        
        // Pre-emptively clean up parameters that we know aren't supported
        if (requestBody is Dictionary<string, object> requestDict)
        {
            // Remove top_k (it's never supported by OpenAI)
            if (requestDict.ContainsKey("top_k"))
            {
                requestDict.Remove("top_k");
                Console.WriteLine("Pre-emptively removed top_k parameter for OpenAI");
            }
            
            // Remove reasoning_effort for non-GPT-4o models
            if (requestDict.ContainsKey("reasoning_effort") && !ModelCode.Contains("gpt-4o"))
            {
                requestDict.Remove("reasoning_effort");
                Console.WriteLine("Pre-emptively removed reasoning_effort parameter for non-GPT-4o model");
            }
        }
        
        var request = CreateRequest(requestBody);
        
        HttpResponseMessage response;
        
        try
        {
            response = await _resilienceService.CreatePluginResiliencePipeline<HttpResponseMessage>()
                .ExecuteAsync(async ct => await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to OpenAI: {ex.Message}");
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
                    await AttemptAutoCorrection(response, currentRequestBody, errorType, errorParam, "OpenAI");
                
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
            await HandleApiErrorAsync(response, "OpenAI");
            yield break;
        }

        var fullResponse = new StringBuilder();
        var tokenCount = 0;

        await foreach (var json in ReadStreamAsync(response, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            string? text = null;
            
            try
            {
                var chunk = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (chunk == null) continue;
                
                if (chunk.TryGetValue("choices", out var choicesObj) && choicesObj is JsonElement choices &&
                    choices[0].TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var content))
                    {
                        text = content.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error processing OpenAI response chunk: {ex.Message}");
            }
            
            if (!string.IsNullOrEmpty(text))
            {
                fullResponse.Append(text);
                tokenCount = EstimateTokenCount(fullResponse.ToString());
                yield return new StreamResponse(text, tokenCount, fullResponse.Length);
            }
        }
    }
    
    private int EstimateTokenCount(string text)
    {
        // OpenAI models use approximately 4 characters per token for English text
        return text.Length / 4;
    }
}