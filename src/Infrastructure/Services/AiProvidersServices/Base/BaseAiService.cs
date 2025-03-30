using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;

namespace Infrastructure.Services.AiProvidersServices.Base;

/// <summary>
/// Content parts for multimodal AI messages
/// </summary>
public abstract record ContentPart;

/// <summary>
/// Represents a text part in a multimodal message
/// </summary>
public record TextPart(string Text) : ContentPart;

/// <summary>
/// Represents an image part in a multimodal message
/// </summary>
public record ImagePart(string MimeType, string Base64Data, string? FileName = null) : ContentPart;

/// <summary>
/// Represents a file attachment in a multimodal message
/// </summary>
public record FilePart(string MimeType, string Base64Data, string FileName) : ContentPart;

/// <summary>
/// Base class for AI model service implementations
/// </summary>
public abstract class BaseAiService : IAiModelService
{
    #region Fields and Constants

    /// <summary>
    /// HTTP client for API communication
    /// </summary>
    protected readonly HttpClient HttpClient;
    
    /// <summary>
    /// API key for the AI provider
    /// </summary>
    protected readonly string ApiKey;
    
    /// <summary>
    /// Model identifier code
    /// </summary>
    protected readonly string ModelCode;
    
    /// <summary>
    /// User-specific model settings
    /// </summary>
    protected readonly UserAiModelSettings? ModelSettings;
    
    /// <summary>
    /// AI model information
    /// </summary>
    protected readonly AiModel? AiModel;
    
    /// <summary>
    /// Custom model parameters (from AI agent if available)
    /// </summary>
    protected readonly ModelParameters? CustomModelParameters;

    /// <summary>
    /// Regular expression for extracting image and file tags from message content
    /// </summary>
    protected static readonly Regex MultimodalTagRegex =
        new(@"<(image|file)-base64:(?:([^:]+):)?([^;>]+);base64,([^>]+)>", RegexOptions.Compiled);

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of the AI service
    /// </summary>
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

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Configures the HTTP client with appropriate headers and settings
    /// </summary>
    protected abstract void ConfigureHttpClient();

    /// <summary>
    /// Gets the API endpoint path for the specific model provider
    /// </summary>
    protected abstract string GetEndpointPath();

    /// <summary>
    /// Streams a response from the AI model
    /// </summary>
    public abstract IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history, 
        CancellationToken cancellationToken);

    #endregion

    #region Content Processing

    /// <summary>
    /// Parses multimodal content from message text, extracting text, images, and files
    /// </summary>
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
                else 
                {
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

    /// <summary>
    /// Prepares the message list for the AI model, including system instructions and chat history
    /// </summary>
    protected virtual List<(string Role, string Content)> PrepareMessageList(IEnumerable<MessageDto> history)
    {
        var messages = new List<(string Role, string Content)>();
        
        // Add system message if available
        var systemMessage = GetSystemMessage();
        if (!string.IsNullOrEmpty(systemMessage))
        {
            messages.Add(("system", systemMessage));
        }
        
        // Add thinking instruction if enabled
        if (ShouldEnableThinking())
        {
            messages.Add(("system", "When solving complex problems, show your step-by-step thinking process marked as '### Thinking:' before the final answer marked as '### Answer:'"));
        }
        
        // Add chat history
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            messages.Add((msg.IsFromAi ? "assistant" : "user", msg.Content.Trim()));
        }
        
        return messages;
    }

    #endregion

    #region Parameter Handling

    /// <summary>
    /// Gets model parameters based on priority: CustomModelParameters > ModelSettings > AiModel defaults
    /// </summary>
    protected virtual Dictionary<string, object> GetModelParameters()
    {
        var parameters = new Dictionary<string, object>();
        
        // Priority 1: Use custom model parameters (from AI agent)
        if (CustomModelParameters != null)
        {
            AddParametersFromModelParameters(parameters, CustomModelParameters);
        }
        // Priority 2: Use user model settings
        else if (ModelSettings != null)
        {
            AddParametersFromUserSettings(parameters, ModelSettings);
        }
        
        // Add max tokens from AI model if not already specified
        if (!parameters.ContainsKey("max_tokens") && AiModel?.MaxOutputTokens.HasValue == true)
        {
            parameters["max_tokens"] = AiModel.MaxOutputTokens.Value;
        }
        
        return parameters;
    }
    
    /// <summary>
    /// Adds parameters from ModelParameters object to the parameters dictionary
    /// </summary>
    private void AddParametersFromModelParameters(Dictionary<string, object> parameters, ModelParameters modelParams)
    {
        if (modelParams.Temperature.HasValue) parameters["temperature"] = modelParams.Temperature.Value;
        if (modelParams.TopP.HasValue) parameters["top_p"] = modelParams.TopP.Value;
        if (modelParams.TopK.HasValue) parameters["top_k"] = modelParams.TopK.Value;
        if (modelParams.FrequencyPenalty.HasValue) parameters["frequency_penalty"] = modelParams.FrequencyPenalty.Value;
        if (modelParams.PresencePenalty.HasValue) parameters["presence_penalty"] = modelParams.PresencePenalty.Value;
        if (modelParams.MaxTokens.HasValue) parameters["max_tokens"] = modelParams.MaxTokens.Value;
        if (modelParams.StopSequences?.Any() == true) parameters["stop"] = modelParams.StopSequences;
    }
    
    /// <summary>
    /// Adds parameters from UserAiModelSettings object to the parameters dictionary
    /// </summary>
    private void AddParametersFromUserSettings(Dictionary<string, object> parameters, UserAiModelSettings settings)
    {
        if (settings.Temperature.HasValue) parameters["temperature"] = settings.Temperature.Value;
        if (settings.TopP.HasValue) parameters["top_p"] = settings.TopP.Value;
        if (settings.TopK.HasValue) parameters["top_k"] = settings.TopK.Value;
        if (settings.FrequencyPenalty.HasValue) parameters["frequency_penalty"] = settings.FrequencyPenalty.Value;
        if (settings.PresencePenalty.HasValue) parameters["presence_penalty"] = settings.PresencePenalty.Value;
        if (settings.StopSequences.Any()) parameters["stop"] = settings.StopSequences;
    }

    /// <summary>
    /// Provider-specific parameter adjustments
    /// </summary>
    protected virtual void AddProviderSpecificParameters(Dictionary<string, object> requestObj) { }

    /// <summary>
    /// Creates the request body for the AI model API call
    /// </summary>
    protected virtual object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["stream"] = true
        };
        
        var parameters = GetModelParameters();
        
        // Process parameters for the specific model
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

    #endregion

    #region HTTP and Streaming

    /// <summary>
    /// Reads streaming data from the HTTP response
    /// </summary>
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

    /// <summary>
    /// Creates an HTTP request with the request body
    /// </summary>
    protected virtual HttpRequestMessage CreateRequest(object requestBody)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody, jsonOptions), 
                Encoding.UTF8, 
                "application/json")
        };
        
        return request;
    }

    #endregion

    #region Error Handling and Recovery

    /// <summary>
    /// Attempts to automatically correct errors in API requests
    /// </summary>
    protected async Task<(bool Success, HttpResponseMessage? RetryResponse, Dictionary<string, object>? CorrectedBody)> 
        AttemptAutoCorrection(
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
        
        // Handle provider-specific corrections
        if (providerName == "OpenAI")
        {
            ApplyOpenAICorrections(correctedBody, errorContent);
        }
        
        // Apply general corrections based on error type and parameter
        if (errorType == "invalid_request_error" && !string.IsNullOrEmpty(errorParam))
        {
            ApplyParameterSpecificCorrections(correctedBody, errorParam, providerName);
        }
        else if (errorContent.Contains("Unrecognized request arguments"))
        {
            ApplyUnrecognizedArgumentsCorrection(correctedBody, errorContent);
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
    
    /// <summary>
    /// Applies OpenAI-specific corrections to the request body
    /// </summary>
    private void ApplyOpenAICorrections(Dictionary<string, object> correctedBody, string errorContent)
    {
        // Always remove top_k for OpenAI as it's not supported
        if (correctedBody.ContainsKey("top_k"))
        {
            correctedBody.Remove("top_k");
            Console.WriteLine("Auto-corrected: Removed unsupported parameter top_k");
        }
        
        // Handle reasoning_effort parameter
        if (errorContent.Contains("reasoning_effort"))
        {
            if (correctedBody.ContainsKey("reasoning_effort"))
            {
                correctedBody.Remove("reasoning_effort");
                Console.WriteLine("Auto-corrected: Removed unsupported parameter reasoning_effort");
            }
        }
    }
    
    /// <summary>
    /// Applies corrections for specific parameters based on error information
    /// </summary>
    private void ApplyParameterSpecificCorrections(Dictionary<string, object> correctedBody, string errorParam, string providerName)
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
                
            // For unknown parameters, try removing them
            default:
                if (correctedBody.ContainsKey(errorParam))
                {
                    correctedBody.Remove(errorParam);
                    Console.WriteLine($"Auto-corrected: Removed problematic parameter '{errorParam}'");
                }
                break;
        }
    }
    
    /// <summary>
    /// Attempts to correct unrecognized arguments errors
    /// </summary>
    private void ApplyUnrecognizedArgumentsCorrection(Dictionary<string, object> correctedBody, string errorContent)
    {
        var argMatches = Regex.Matches(errorContent, "Unrecognized request arguments supplied: ([^,\"]+)");
        
        if (argMatches.Count > 0)
        {
            foreach (Match match in argMatches)
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

    /// <summary>
    /// Handles API errors by extracting and formatting error information
    /// </summary>
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

    #endregion

    #region Model Capabilities and Settings

    /// <summary>
    /// Gets the system message to use for the conversation
    /// </summary>
    protected string GetSystemMessage() => 
        ModelSettings?.SystemMessage ?? "Always respond using markdown formatting";

    /// <summary>
    /// Determines if thinking mode should be enabled based on model capabilities
    /// </summary>
    protected bool ShouldEnableThinking() =>
        AiModel?.SupportsThinking == true;

    /// <summary>
    /// Checks if the current model is an OpenAI model
    /// </summary>
    protected bool IsOpenAIModel() =>
        ModelCode.Contains("gpt") || ModelCode.Contains("text-embedding") || ModelCode.Contains("dall-e");
    
    /// <summary>
    /// Checks if the current model is an Anthropic model
    /// </summary>
    protected bool IsAnthropicModel() =>
        ModelCode.Contains("claude");
    
    /// <summary>
    /// Checks if the current model is a Google Gemini model
    /// </summary>
    protected bool IsGeminiModel() =>
        ModelCode.Contains("gemini");
    
    /// <summary>
    /// Checks if the current model is a DeepSeek model
    /// </summary>
    protected bool IsDeepSeekModel() =>
        ModelCode.Contains("deepseek");
    
    /// <summary>
    /// Determines if the model is cloud-hosted vs. locally hosted
    /// </summary>
    protected bool IsCloudHostedModel() =>
        IsOpenAIModel() || IsAnthropicModel() || IsGeminiModel() || IsDeepSeekModel();
    
    /// <summary>
    /// Checks if a parameter is supported by the current model
    /// </summary>
    protected virtual bool SupportsParameter(string paramName)
    {
        // Default supported parameters across most models
        var commonSupportedParams = new HashSet<string> { "model", "messages", "stream", "system" };

        if (commonSupportedParams.Contains(paramName))
            return true;

        // Handle model-specific parameter support
        if (IsOpenAIModel())
        {
            return IsParameterSupportedByOpenAI(paramName);
        }
        else if (IsAnthropicModel())
        {
            return IsParameterSupportedByAnthropic(paramName);
        }
        else if (IsGeminiModel())
        {
            return IsParameterSupportedByGemini(paramName);
        }
        else if (IsDeepSeekModel())
        {
            return IsParameterSupportedByDeepSeek(paramName);
        }

        // Fallback for unknown models: assume supported
        Console.WriteLine($"Warning: Unknown model type for parameter support check ('{paramName}'). Assuming supported.");
        return true;
    }
    
    /// <summary>
    /// Checks if a parameter is supported by OpenAI models
    /// </summary>
    private bool IsParameterSupportedByOpenAI(string paramName)
    {
        // Explicitly list supported OpenAI parameters
        var openAiSupported = new HashSet<string> {
            "temperature", "top_p", "frequency_penalty", "presence_penalty",
            "max_tokens", "stop", "seed", "response_format", "tools", "tool_choice"
        };

        // Get the provider-specific parameter name
        string providerParamName = GetProviderParameterName(paramName);

        if (openAiSupported.Contains(providerParamName)) 
            return true;

        // Specifically block known unsupported parameters
        if (providerParamName is "top_k" or "topP" or "topK" or "maxOutputTokens") 
            return false;

        // Assume unsupported if not explicitly listed
        Console.WriteLine($"Parameter '{paramName}' (mapped to '{providerParamName}') is not explicitly supported by OpenAI. Assuming unsupported.");
        return false;
    }
    
    /// <summary>
    /// Checks if a parameter is supported by Anthropic models
    /// </summary>
    private bool IsParameterSupportedByAnthropic(string paramName)
    {
        var anthropicSupported = new HashSet<string> { 
            "max_tokens", "temperature", "top_k", "top_p", "stop_sequences", "tools" 
        };
        
        return anthropicSupported.Contains(paramName);
    }
    
    /// <summary>
    /// Checks if a parameter is supported by Google Gemini models
    /// </summary>
    private bool IsParameterSupportedByGemini(string paramName)
    {
        // Use the provider-specific names for Gemini check
        string providerParamName = GetProviderParameterName(paramName);
        var geminiSupported = new HashSet<string> { 
            "temperature", "topP", "topK", "maxOutputTokens", "stopSequences", "safetySettings" 
        };
        
        return geminiSupported.Contains(providerParamName);
    }
    
    /// <summary>
    /// Checks if a parameter is supported by DeepSeek models
    /// </summary>
    private bool IsParameterSupportedByDeepSeek(string paramName)
    {
        var deepSeekSupported = new HashSet<string> {
            "temperature", "top_p", "max_tokens", "stop", "frequency_penalty", "presence_penalty"
        };
        
        return deepSeekSupported.Contains(paramName);
    }
    
    /// <summary>
    /// Maps standard parameter names to provider-specific parameter names
    /// </summary>
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

    #endregion
}