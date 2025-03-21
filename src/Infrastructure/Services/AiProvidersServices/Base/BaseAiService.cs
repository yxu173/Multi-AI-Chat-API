using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;

namespace Infrastructure.Services.AiProvidersServices.Base;

public abstract class BaseAiService : IAiModelService
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiKey;
    protected readonly string ModelCode;
    protected readonly UserAiModelSettings? ModelSettings;
    protected readonly AiModel? AiModel;

    protected BaseAiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        string baseUrl,
        UserAiModelSettings? modelSettings = null,
        AiModel? aiModel = null)
    {
        HttpClient = httpClientFactory.CreateClient();
        HttpClient.BaseAddress = new Uri(baseUrl);
        ApiKey = apiKey;
        ModelCode = modelCode;
        ModelSettings = modelSettings;
        AiModel = aiModel;

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
    
   
    protected Dictionary<string, object> ApplyUserSettings(Dictionary<string, object> requestObj, bool includeStopSequences = true)
    {
       
        if (AiModel != null)
        {
            if (AiModel.MaxOutputTokens.HasValue)
            {
                if (!requestObj.ContainsKey("max_tokens"))
                    requestObj["max_tokens"] = AiModel.MaxOutputTokens.Value;
                if (!requestObj.ContainsKey("maxOutputTokens"))
                    requestObj["maxOutputTokens"] = AiModel.MaxOutputTokens.Value;
                if (!requestObj.ContainsKey("max_output_tokens"))
                    requestObj["max_output_tokens"] = AiModel.MaxOutputTokens.Value;
            }
        }
        
        
        if (ModelSettings == null)
        {
           
            if (!requestObj.ContainsKey("max_tokens") && !requestObj.ContainsKey("maxOutputTokens") && !requestObj.ContainsKey("max_output_tokens"))
                requestObj["max_tokens"] = 2000;
            
            return requestObj;
        }
        
      
        var settingsToApply = new Dictionary<string, object>();
        
    
        
        if (ModelSettings.Temperature.HasValue)
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
        
     
        foreach (var setting in settingsToApply)
        {
            if (requestObj.ContainsKey(setting.Key))
            {
                requestObj[setting.Key] = setting.Value;
            }
            else
            {
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
        
        return requestObj;
    }
}