using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

public class DeepSeekService : BaseAiService
{
    private const string BaseUrl = "https://api.deepseek.com/v1/";

    public DeepSeekService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null, ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters) { }

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
            messages.Add(("system", "When solving complex problems, please show your detailed step-by-step thinking process marked as '### Thinking:' before providing the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly."));
        }
        string lastRole = messages.Count > 0 ? "system" : null;
        MessageDto? pendingMsg = null;
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            string currentRole = msg.IsFromAi ? "assistant" : "user";
            if (currentRole == lastRole && pendingMsg != null)
            {
                pendingMsg = new MessageDto(
                    pendingMsg.Content + "\n\n" + msg.Content.Trim(),
                    pendingMsg.IsFromAi,
                    pendingMsg.MessageId,
                    pendingMsg.FileAttachments,
                    pendingMsg.Base64Content
                );
            }
            else
            {
                if (pendingMsg != null)
                {
                    messages.Add((lastRole, pendingMsg.Content.Trim()));
                }
                pendingMsg = msg;
                lastRole = currentRole;
            }
        }
        if (pendingMsg != null)
        {
            messages.Add((lastRole, pendingMsg.Content.Trim()));
        }
        return messages;
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history)
            .Select(m => new { role = m.Role, content = m.Content }).ToList();
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);
        
        // Define known supported parameters for DeepSeek models
        var standardSupportedParams = new HashSet<string>() 
        { 
            "model", "messages", "stream", "temperature", "top_p", 
            "max_tokens", "enable_cot", "enable_reasoning", "reasoning_mode" 
        };
        
        // Remove unsupported parameters
        var keysToRemove = requestObj.Keys
            .Where(k => !standardSupportedParams.Contains(k))
            .ToList();
            
        foreach (var key in keysToRemove)
        {
            Console.WriteLine($"Preemptively removing unsupported parameter for DeepSeek: {key}");
            requestObj.Remove(key);
        }
        
        requestObj["messages"] = messages;
        return requestObj;
    }

    protected override void AddProviderSpecificParameters(Dictionary<string, object> requestObj)
    {
        if (ShouldEnableThinking())
        {
            requestObj["enable_cot"] = true;
            requestObj["enable_reasoning"] = true;
            requestObj["reasoning_mode"] = "chain_of_thought";
        }
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(CreateRequestBody(history));
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) { await HandleApiErrorAsync(response, "DeepSeek"); yield break; }

        var fullResponse = new StringBuilder();
        await foreach (var json in ReadStreamAsync(response, cancellationToken))
        {
           
                var chunk = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (chunk.TryGetValue("choices", out var choicesObj) && choicesObj is JsonElement choices && choices[0].TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrEmpty(text)) fullResponse.Append(text);
                        yield return new StreamResponse(text, 0, fullResponse.Length / 4);
                    }
                }
            
        }
    }
}