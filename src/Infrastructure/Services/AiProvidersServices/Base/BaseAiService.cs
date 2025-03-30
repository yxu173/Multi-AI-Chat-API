using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;

namespace Infrastructure.Services.AiProvidersServices.Base;

// Add these record types within the namespace or inside the BaseAiService class
public abstract record ContentPart;
public record TextPart(string Text) : ContentPart;
public record ImagePart(string MimeType, string Base64Data, string? FileName = null) : ContentPart; // Added optional FileName
public record FilePart(string MimeType, string Base64Data, string FileName) : ContentPart; // Assuming FileName is always present for files

public abstract class BaseAiService : IAiModelService
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiKey;
    protected readonly string ModelCode;
    protected readonly UserAiModelSettings? ModelSettings;
    protected readonly AiModel? AiModel;
    protected readonly ModelParameters? CustomModelParameters;

    // Regex to find image or file tags
    protected static readonly Regex MultimodalTagRegex =
        new Regex(@"<(image|file)-base64:(?:([^:]+):)?([^;>]+);base64,([^>]+)>", RegexOptions.Compiled);

    protected BaseAiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        string baseUrl,
        UserAiModelSettings? modelSettings = null,
        AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
    {
        HttpClient = httpClientFactory.CreateClient();
        HttpClient.BaseAddress = new Uri(baseUrl);
        ApiKey = apiKey;
        ModelCode = modelCode;
        ModelSettings = modelSettings;
        AiModel = aiModel;
        CustomModelParameters = customModelParameters;
        ConfigureHttpClient();
    }

    // Abstract methods for provider-specific behavior
    protected abstract void ConfigureHttpClient();
    protected abstract string GetEndpointPath();
    public abstract IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history, CancellationToken cancellationToken);

    // Standard message preparation
    protected virtual List<ContentPart> ParseMultimodalContent(string messageContent)
    {
        var contentParts = new List<ContentPart>();
        var lastIndex = 0;

        foreach (Match match in MultimodalTagRegex.Matches(messageContent))
        {
            // Add text part before the tag if any
            if (match.Index > lastIndex)
            {
                string textBefore = messageContent.Substring(lastIndex, match.Index - lastIndex).Trim();
                if (!string.IsNullOrEmpty(textBefore))
                {
                    contentParts.Add(new TextPart(textBefore));
                }
            }

            string tagType = match.Groups[1].Value;
            string fileNameOrMime = match.Groups[2].Value; // Might be filename (file) or empty (image)
            string mimeTypeOrFileName = match.Groups[3].Value; // Might be mime (image/file) or filename (old file format)
            string base64Data = match.Groups[4].Value;

            if (tagType == "image")
            {
                // Assuming format <image-base64:mimeType;base64,data>
                string mimeType = mimeTypeOrFileName;
                contentParts.Add(new ImagePart(mimeType, base64Data));
            }
            else if (tagType == "file")
            {
                // Assuming format <file-base64:fileName:mimeType;base64,data>
                string fileName = fileNameOrMime;
                string mimeType = mimeTypeOrFileName;
                // Add basic validation
                if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(mimeType))
                {
                     contentParts.Add(new FilePart(mimeType, base64Data, fileName));
                }
                else {
                    // Fallback or log error if format is unexpected
                     contentParts.Add(new TextPart($"[Malformed file tag: {match.Value}]"));
                }
            }

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text part after the last tag
        if (lastIndex < messageContent.Length)
        {
            string textAfter = messageContent.Substring(lastIndex).Trim();
            if (!string.IsNullOrEmpty(textAfter))
            {
                contentParts.Add(new TextPart(textAfter));
            }
        }

        // If no tags were found and content exists, treat it as pure text
        if (!contentParts.Any() && !string.IsNullOrWhiteSpace(messageContent))
        {
            contentParts.Add(new TextPart(messageContent.Trim()));
        }

        return contentParts;
    }

    protected virtual List<(string Role, string Content)> PrepareMessageList(IEnumerable<MessageDto> history)
    {
        var messages = new List<(string Role, string Content)>();
        var systemMessage = GetSystemMessage();
        if (!string.IsNullOrEmpty(systemMessage))
        {
            messages.Add(("system", systemMessage));
        }
        if (ShouldEnableThinking())
        {
            messages.Add(("system", "When solving complex problems, show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'"));
        }
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            messages.Add((msg.IsFromAi ? "assistant" : "user", msg.Content.Trim()));
        }
        return messages;
    }

    // Standard parameter collection
    protected virtual Dictionary<string, object> GetModelParameters()
    {
        var parameters = new Dictionary<string, object>();
        if (CustomModelParameters != null)
        {
            if (CustomModelParameters.Temperature.HasValue) parameters["temperature"] = CustomModelParameters.Temperature.Value;
            if (CustomModelParameters.TopP.HasValue) parameters["top_p"] = CustomModelParameters.TopP.Value;
            if (CustomModelParameters.TopK.HasValue) parameters["top_k"] = CustomModelParameters.TopK.Value;
            if (CustomModelParameters.FrequencyPenalty.HasValue) parameters["frequency_penalty"] = CustomModelParameters.FrequencyPenalty.Value;
            if (CustomModelParameters.PresencePenalty.HasValue) parameters["presence_penalty"] = CustomModelParameters.PresencePenalty.Value;
            if (CustomModelParameters.MaxTokens.HasValue) parameters["max_tokens"] = CustomModelParameters.MaxTokens.Value;
            if (CustomModelParameters.StopSequences?.Any() == true) parameters["stop"] = CustomModelParameters.StopSequences;
        }
        else if (ModelSettings != null)
        {
            if (ModelSettings.Temperature.HasValue) parameters["temperature"] = ModelSettings.Temperature.Value;
            if (ModelSettings.TopP.HasValue) parameters["top_p"] = ModelSettings.TopP.Value;
            if (ModelSettings.TopK.HasValue) parameters["top_k"] = ModelSettings.TopK.Value;
            if (ModelSettings.FrequencyPenalty.HasValue) parameters["frequency_penalty"] = ModelSettings.FrequencyPenalty.Value;
            if (ModelSettings.PresencePenalty.HasValue) parameters["presence_penalty"] = ModelSettings.PresencePenalty.Value;
            if (ModelSettings.StopSequences.Any()) parameters["stop"] = ModelSettings.StopSequences;
        }
        if (!parameters.ContainsKey("max_tokens") && AiModel?.MaxOutputTokens.HasValue == true)
        {
            parameters["max_tokens"] = AiModel.MaxOutputTokens.Value;
        }
        return parameters;
    }

    // Virtual method for provider-specific request tweaks
    protected virtual void AddProviderSpecificParameters(Dictionary<string, object> requestObj) { }

    // Common request body creation
    protected virtual object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["stream"] = true
        };
        
        var parameters = GetModelParameters();
        
        // Filter parameters based on model support and transform parameter names if needed
        foreach (var param in parameters)
        {
            string providerParamName = GetProviderParameterName(param.Key);
            
            // Skip parameters not supported by this model
            if (!SupportsParameter(providerParamName))
            {
                Console.WriteLine($"Skipping unsupported parameter '{param.Key}' for model {ModelCode}");
                continue;
            }
            
            // Add the parameter with the provider-specific name
            requestObj[providerParamName] = param.Value;
        }
        
        // Add provider-specific parameters
        AddProviderSpecificParameters(requestObj);
        
        return requestObj;
    }

    // Common SSE streaming utility
    protected async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data: ") == true)
            {
                var json = line["data: ".Length..];
                if (json != "[DONE]") yield return json;
            }
        }
    }

    protected virtual HttpRequestMessage CreateRequest(object requestBody)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath())
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json")
        };
        return request;
    }

    protected async Task<(bool Success, HttpResponseMessage? RetryResponse, Dictionary<string, object>? CorrectedBody)> AttemptAutoCorrection(
        HttpResponseMessage response, 
        object originalRequestBody,
        string errorType,
        string errorParam,
        string providerName)
    {
        if (originalRequestBody is not Dictionary<string, object> correctedBody)
        {
            return (false, null, null);
        }
        
        // Create a deep copy to avoid modifying the original
        correctedBody = new Dictionary<string, object>(correctedBody);
        
        // Get response content to analyze the error
        string errorContent = await response.Content.ReadAsStringAsync();
        
        // Handle OpenAI specific parameters
        if (providerName == "OpenAI")
        {
            // Always remove top_k for OpenAI as it's not supported
            if (correctedBody.ContainsKey("top_k"))
            {
                correctedBody.Remove("top_k");
                Console.WriteLine("Auto-corrected: Removed unsupported parameter top_k");
            }
            
            // Handle reasoning_effort parameter
            if (errorContent.Contains("reasoning_effort") && providerName == "OpenAI")
            {
                if (correctedBody.ContainsKey("reasoning_effort"))
                {
                    correctedBody.Remove("reasoning_effort");
                    Console.WriteLine("Auto-corrected: Removed unsupported parameter reasoning_effort");
                }
            }
        }
        
        // Try to auto-correct known issues based on error message
        if (errorType == "invalid_request_error" && !string.IsNullOrEmpty(errorParam))
        {
            switch (errorParam)
            {
                // Handle typical parameter errors
                case "top_k" when providerName == "OpenAI":
                    correctedBody.Remove("top_k");
                    Console.WriteLine("Auto-corrected: Removed unsupported parameter top_k");
                    break;
                    
                case "reasoning_effort" when providerName == "OpenAI":
                    correctedBody.Remove("reasoning_effort");
                    Console.WriteLine("Auto-corrected: Removed unsupported parameter reasoning_effort");
                    break;
                
                case "temperature":
                    if (correctedBody.TryGetValue("temperature", out var tempValue) && tempValue is double temp)
                    {
                        // Fix temperature out of range
                        if (temp < 0) 
                        {
                            correctedBody["temperature"] = 0.0;
                            Console.WriteLine($"Auto-corrected: temperature from {temp} to 0.0");
                        }
                        else if (temp > 2)
                        {
                            correctedBody["temperature"] = 1.0;
                            Console.WriteLine($"Auto-corrected: temperature from {temp} to 1.0");
                        }
                    }
                    break;
                    
                // Add more parameter-specific corrections here
                
                default:
                    // For unknown parameters, try removing them
                    if (correctedBody.ContainsKey(errorParam))
                    {
                        correctedBody.Remove(errorParam);
                        Console.WriteLine($"Auto-corrected: Removed problematic parameter '{errorParam}'");
                    }
                    break;
            }
        }
        else if (errorContent.Contains("Unrecognized request arguments"))
        {
            // Handle unrecognized arguments error
            var argMatches = System.Text.RegularExpressions.Regex.Matches(errorContent, "Unrecognized request arguments supplied: ([^,\"]+)");
            
            if (argMatches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in argMatches)
                {
                    string[] invalidParams = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .ToArray();
                        
                    foreach (var param in invalidParams)
                    {
                        if (correctedBody.ContainsKey(param))
                        {
                            correctedBody.Remove(param);
                            Console.WriteLine($"Auto-corrected: Removed unrecognized parameter '{param}'");
                        }
                    }
                }
            }
        }
        
        // Attempt request with corrected parameters
        var retryRequest = CreateRequest(correctedBody);
        
        try
        {
            var retryResponse = await HttpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead);
            
            // If we got a successful response, return it
            if (retryResponse.IsSuccessStatusCode)
            {
                return (true, retryResponse, correctedBody);
            }
            
            // Otherwise, return that the correction failed
            Console.WriteLine($"Auto-correction attempted but still got error: {retryResponse.StatusCode}");
            return (false, null, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during auto-correction request: {ex.Message}");
            return (false, null, null);
        }
    }

    protected async Task HandleApiErrorAsync(HttpResponseMessage response, string providerName)
    {
        try
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            
            // Try to extract specific error information for better diagnostics
            string detailedError = errorContent;
            string errorType = "unknown";
            string apiErrorMessage = errorContent;
            string errorParam = "none";
            
            try
            {
                var errorJson = JsonSerializer.Deserialize<JsonElement>(errorContent);
                if (errorJson.TryGetProperty("error", out var errorObj))
                {
                    // Extract error details
                    errorType = errorObj.TryGetProperty("type", out var type) ? type.GetString() ?? "unknown" : "unknown";
                    apiErrorMessage = errorObj.TryGetProperty("message", out var message) ? message.GetString() ?? errorContent : errorContent;
                    errorParam = errorObj.TryGetProperty("param", out var param) ? param.GetString() ?? "none" : "none";
                    
                    Console.WriteLine($"API Error Details - Provider: {providerName}, Type: {errorType}, Param: {errorParam}, Message: {apiErrorMessage}");
                    
                    detailedError = $"{apiErrorMessage} (Parameter: {errorParam}, Type: {errorType})";
                }
            }
            catch
            {
                // If error parsing fails, use the raw content
                detailedError = errorContent;
            }
            
            var errorMessage = $"{providerName} API Error: {response.StatusCode} - {detailedError}";
            
            // Log detailed error information
            Console.WriteLine(errorMessage);
            
            throw new Exception(errorMessage);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex.GetType().Name != "Exception")
        {
            // Handle error response parsing errors
            throw new Exception($"{providerName} API Error: {response.StatusCode} - Unable to parse error response: {ex.Message}");
        }
    }

    protected string GetSystemMessage() => ModelSettings?.SystemMessage ?? "Always respond using markdown formatting";

    protected bool ShouldEnableThinking() =>
        AiModel?.SupportsThinking == true;

    // Helper methods for detecting model families
    protected bool IsOpenAIModel()
    {
        return ModelCode.Contains("gpt") || ModelCode.Contains("text-embedding") || ModelCode.Contains("dall-e");
    }
    
    protected bool IsAnthropicModel()
    {
        return ModelCode.Contains("claude");
    }
    
    protected bool IsGeminiModel()
    {
        return ModelCode.Contains("gemini");
    }
    
    protected bool IsDeepSeekModel()
    {
        return ModelCode.Contains("deepseek");
    }
    
    // Method to determine if a model is an API-hosted model vs. a local model
    protected bool IsCloudHostedModel()
    {
        return IsOpenAIModel() || IsAnthropicModel() || IsGeminiModel() || IsDeepSeekModel();
    }
    
    // Method to check if a model supports a specific parameter
    protected virtual bool SupportsParameter(string paramName)
    {
        // Default supported parameters across most models
        var commonSupportedParams = new HashSet<string> { "model", "messages", "stream", "system" }; // Added system as common

        if (commonSupportedParams.Contains(paramName))
            return true;

        // OpenAI GPT models generally support these parameters
        if (IsOpenAIModel())
        {
            // Explicitly list supported OpenAI parameters
            var openAiSupported = new HashSet<string> {
                "temperature", "top_p", "frequency_penalty", "presence_penalty",
                "max_tokens", "stop", "seed", "response_format", "tools", "tool_choice" // Added common tool use params
                // Note: max_tokens might be mapped to max_completion_tokens by GetProviderParameterName
            };

            // Check against the provider-specific name from GetProviderParameterName
            string providerParamName = GetProviderParameterName(paramName);

            if (openAiSupported.Contains(providerParamName)) return true;

            // Specifically block top_k for OpenAI
            if (providerParamName == "top_k") return false;

            // Block other known unsupported or potentially problematic params
            if (new[] { "topP", "topK", "maxOutputTokens" }.Contains(providerParamName)) return false; // Gemini names

            // Fallback for OpenAI: assume unsupported unless explicitly listed? Or rely on API error?
            // Let's be stricter for OpenAI and assume unsupported if not listed.
            Console.WriteLine($"Parameter '{paramName}' (mapped to '{providerParamName}') is not explicitly supported by OpenAI in this configuration. Assuming unsupported.");
            return false;
        }

        // Anthropic Claude models
        if (IsAnthropicModel())
        {
            var anthropicSupported = new HashSet<string> { "max_tokens", "temperature", "top_k", "top_p", "stop_sequences", "tools" }; // Added tools
            return anthropicSupported.Contains(paramName);
        }

        // Gemini models
        if (IsGeminiModel())
        {
            // Use the provider-specific names for Gemini check
            string providerParamName = GetProviderParameterName(paramName);
            var geminiSupported = new HashSet<string> { "temperature", "topP", "topK", "maxOutputTokens", "stopSequences", "safetySettings" };
            return geminiSupported.Contains(providerParamName);
        }

        // DeepSeek models (check their documentation for specific supported params)
        if (IsDeepSeekModel())
        {
            var deepSeekSupported = new HashSet<string> {
                "temperature", "top_p", "max_tokens", "stop", "frequency_penalty", "presence_penalty"
                // Add other supported params like enable_cot, reasoning_mode if applicable
            };
            return deepSeekSupported.Contains(paramName);
        }

        // Fallback for any unknown model provider: return true and let the API decide.
        Console.WriteLine($"Warning: Unknown model type for parameter support check ('{paramName}'). Assuming supported.");
        return true;
    }
    
    // Method to get a provider-appropriate name for a parameter
    protected virtual string GetProviderParameterName(string standardName)
    {
        if (IsOpenAIModel())
        {
            // OpenAI specific parameter mappings
            return standardName switch
            {
                "max_tokens" => "max_completion_tokens",
                _ => standardName
            };
        }
        
        if (IsGeminiModel())
        {
            // Gemini specific parameter mappings
            return standardName switch
            {
                "top_p" => "topP",
                "top_k" => "topK",
                "max_tokens" => "maxOutputTokens",
                _ => standardName
            };
        }
        
        // Default to original name
        return standardName;
    }
}