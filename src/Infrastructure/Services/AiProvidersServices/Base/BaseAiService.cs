using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;

namespace Infrastructure.Services.AiProvidersServices.Base;

public abstract class BaseAiService : IAiModelService
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiKey;
    protected readonly string ModelCode;
    protected readonly UserAiModelSettings? ModelSettings;
    protected readonly AiModel? AiModel;
    protected readonly ModelParameters? CustomModelParameters;

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

  
    protected abstract void ConfigureHttpClient();

   
    protected abstract object CreateRequestBody(IEnumerable<MessageDto> history);

  
    protected virtual HttpRequestMessage CreateRequest(object requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath());
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        return request;
    }

   
    protected abstract string GetEndpointPath();

    
    public abstract IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history,
        CancellationToken cancellationToken);

   
    protected async Task HandleApiErrorAsync(HttpResponseMessage response, string providerName)
    {
        var errorContent = await response.Content.ReadAsStringAsync();
        throw new Exception($"{providerName} API Error: {response.StatusCode} - {errorContent}");
    }
    
  
    protected string GetSystemMessage()
    {
        if (ModelSettings?.SystemMessage != null)
        {
            return ModelSettings.SystemMessage;
        }
        
        return "Always respond using markdown formatting";
    }
    
    
    protected List<MessageDto> ApplySystemMessageToHistory(IEnumerable<MessageDto> history)
    {
        var messages = history.ToList();
        
        // Check if the first message is already a system message
        bool hasSystemMessage = messages.Count > 0 && 
                               messages[0].Content.StartsWith("system:") || 
                               messages[0].Content.StartsWith("System:");
        
        // If not, add our system message
        if (!hasSystemMessage)
        {
            var systemMessage = GetSystemMessage();
            messages.Insert(0, new MessageDto("system: " + systemMessage, true, Guid.NewGuid()));
        }
        
        return messages;
    }
   
    protected Dictionary<string, object> ApplyUserSettings(Dictionary<string, object> requestObj, bool includeStopSequences = true, bool applyTemperature = true)
    {
        // If custom model options are provided, apply them first
        if (CustomModelParameters != null)
        {
            ApplyCustomModelOptions(requestObj, CustomModelParameters, applyTemperature);
        }
       
        // Then apply model default limits if available
        if (AiModel != null)
        {
            if (AiModel.MaxOutputTokens.HasValue && !CustomModelParameters?.MaxTokens.HasValue == true)
            {
                if (!requestObj.ContainsKey("max_tokens"))
                    requestObj["max_tokens"] = AiModel.MaxOutputTokens.Value;
                if (!requestObj.ContainsKey("maxOutputTokens"))
                    requestObj["maxOutputTokens"] = AiModel.MaxOutputTokens.Value;
                if (!requestObj.ContainsKey("max_output_tokens"))
                    requestObj["max_output_tokens"] = AiModel.MaxOutputTokens.Value;
            }
        }
        
        // If no user settings or custom options, set some reasonable defaults
        if (ModelSettings == null && CustomModelParameters == null)
        {
            if (!requestObj.ContainsKey("max_tokens") && !requestObj.ContainsKey("maxOutputTokens") && !requestObj.ContainsKey("max_output_tokens"))
                requestObj["max_tokens"] = 2000;
            
            return requestObj;
        }
        
        // If we have user settings but no custom options, apply the user settings
        if (ModelSettings != null && CustomModelParameters == null)
        {
            var settingsToApply = new Dictionary<string, object>();
            
            if (ModelSettings.Temperature.HasValue && applyTemperature)
            {
                settingsToApply["temperature"] = ModelSettings.Temperature.Value;
            }
            
            if (ModelSettings.TopP.HasValue)
            {
                settingsToApply["top_p"] = ModelSettings.TopP.Value;
                settingsToApply["topP"] = ModelSettings.TopP.Value;
            }
            
            if (ModelSettings.TopK.HasValue)
            {
                settingsToApply["top_k"] = ModelSettings.TopK.Value;
                settingsToApply["topK"] = ModelSettings.TopK.Value;
            }
            
            if (ModelSettings.FrequencyPenalty.HasValue)
            {
                settingsToApply["frequency_penalty"] = ModelSettings.FrequencyPenalty.Value;
            }
            
            if (ModelSettings.PresencePenalty.HasValue)
            {
                settingsToApply["presence_penalty"] = ModelSettings.PresencePenalty.Value;
            }
            
            if (includeStopSequences && ModelSettings.StopSequences.Any())
            {
                settingsToApply["stop"] = ModelSettings.StopSequences;
                settingsToApply["stop_sequences"] = ModelSettings.StopSequences;
            }
            
            // Apply the settings
            foreach (var setting in settingsToApply)
            {
                if (requestObj.ContainsKey(setting.Key))
                {
                    requestObj[setting.Key] = setting.Value;
                }
                else
                {
                    // Only add key if it's a standard parameter
                    switch (setting.Key)
                    {
                        case "max_tokens":
                        case "temperature":
                        case "top_p":
                        case "top_k":
                        case "frequency_penalty":
                        case "presence_penalty":
                        case "stop":
                            requestObj[setting.Key] = setting.Value;
                            break;
                    }
                }
            }
        }
        
        return requestObj;
    }

    private void ApplyCustomModelOptions(Dictionary<string, object> requestObj, ModelParameters options, bool applyTemperature = true)
    {
        if (options.Temperature.HasValue && applyTemperature)
        {
            requestObj["temperature"] = options.Temperature.Value;
        }
        
        if (options.TopP.HasValue)
        {
            requestObj["top_p"] = options.TopP.Value;
            requestObj["topP"] = options.TopP.Value;
        }
        
        if (options.TopK.HasValue)
        {
            requestObj["top_k"] = options.TopK.Value;
            requestObj["topK"] = options.TopK.Value;
        }
        
        if (options.FrequencyPenalty.HasValue)
        {
            requestObj["frequency_penalty"] = options.FrequencyPenalty.Value;
        }
        
        if (options.PresencePenalty.HasValue)
        {
            requestObj["presence_penalty"] = options.PresencePenalty.Value;
        }
        
        if (options.MaxTokens.HasValue)
        {
            requestObj["max_tokens"] = options.MaxTokens.Value;
            requestObj["maxOutputTokens"] = options.MaxTokens.Value;
            requestObj["max_output_tokens"] = options.MaxTokens.Value;
        }
        
        if (options.StopSequences != null && options.StopSequences.Any())
        {
            requestObj["stop"] = options.StopSequences;
            requestObj["stop_sequences"] = options.StopSequences;
        }

        // Context window size - model specific implementation may override
        if (!string.IsNullOrEmpty(options.ContextLimit))
        {
            requestObj["context_limit"] = options.ContextLimit;
        }

        // Apply thinking if supported
        if (AiModel?.SupportsThinking == true && ShouldEnableThinking())
        {
            requestObj["enable_thinking"] = true;
            requestObj["enable_cot"] = true;
            
            // Each model might have a different way to enable thinking
            // Use a simpler structure to avoid nesting issues
            requestObj["thinking"] = new Dictionary<string, object> {
                { "type", "enabled" }
            };
        }

        // Reasoning effort (for models that support it)
        if (options.ReasoningEffort.HasValue)
        {
            requestObj["reasoning_effort"] = options.ReasoningEffort.Value;
        }

        // Safety settings
        if (!string.IsNullOrEmpty(options.SafetySettings))
        {
            try {
                var safetySettings = JsonSerializer.Deserialize<object>(options.SafetySettings);
                if (safetySettings != null)
                {
                    requestObj["safety_settings"] = safetySettings;
                }
            }
            catch {
                // Ignore parsing errors for safety settings
            }
        }
    }

    /// <summary>
    /// Helper method to determine if thinking mode should be enabled based on model and user settings
    /// </summary>
    protected bool ShouldEnableThinking()
    {
        // Only enable thinking if the model supports it
        if (AiModel?.SupportsThinking != true)
            return false;
            
        // Check custom parameters
        if (CustomModelParameters?.EnableThinking.HasValue == true)
            return CustomModelParameters.EnableThinking.Value;
           
        // By default, enable thinking for models that support it
        return true;
    }
}