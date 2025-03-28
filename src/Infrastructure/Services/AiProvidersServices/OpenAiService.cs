using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;
using Tiktoken;

public class OpenAiService : BaseAiService
{
    private const string BaseUrl = "https://api.openai.com/v1/";
    private readonly IResilienceService _resilienceService;
    private static readonly Regex ImageBase64Regex = new Regex(@"<image-base64:(.*?);base64,(.*?)>", RegexOptions.Compiled);
    private static readonly Regex FileBase64Regex = new Regex(@"<file-base64:(.*?):(.*?);base64,(.*?)>", RegexOptions.Compiled);

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
            if (msg.Content.Contains("<image-base64:") || msg.Content.Contains("<file-base64:"))
            {
                messages.Add(ProcessMultimodalMessage(msg));
            }
            else
            {
                messages.Add((msg.IsFromAi ? "assistant" : "user", msg.Content.Trim()));
            }
        }
        
        return messages;
    }

    private (string Role, string Content) ProcessMultimodalMessage(MessageDto message)
    {
        if (!message.Content.Contains("<image-base64:") && !message.Content.Contains("<file-base64:"))
        {
            return (message.IsFromAi ? "assistant" : "user", message.Content.Trim());
        }

         return (message.IsFromAi ? "assistant" : "user", message.Content.Trim());
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
        if (CustomModelParameters?.ReasoningEffort.HasValue == true)
        {
            return CustomModelParameters.ReasoningEffort.Value switch
            {
                <= 33 => "low",
                >= 66 => "high",
                _ => "medium"
            };
        }
        
        
        return "medium";
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messagesList = PrepareMessageList(history);
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);
        
        var parametersToRemove = new List<string>();
        
        if (ShouldEnableThinking())
        {
            parametersToRemove.AddRange(new[] {
                "temperature", "top_p", "top_k", "frequency_penalty", 
                "presence_penalty", "reasoning_effort", "seed", "response_format"
            });
            
        }
        
        parametersToRemove.Add("top_k");
        
        foreach (var param in parametersToRemove)
        {
            if (requestObj.ContainsKey(param))
            {
                requestObj.Remove(param);
                Console.WriteLine($"Preemptively removed {param} parameter for model {ModelCode}");
            }
        }
        
        if (requestObj.ContainsKey("max_tokens"))
        {
            var maxTokensValue = requestObj["max_tokens"];
            requestObj.Remove("max_tokens");
            requestObj["max_completion_tokens"] = maxTokensValue;
        }
        
        var processedMessages = new List<object>();
        foreach (var (role, content) in messagesList)
        {
            if (content.Contains("<image-base64:") || content.Contains("<file-base64:"))
            {
                var contentItems = CreateMultimodalContent(content);
                processedMessages.Add(new { role, content = contentItems });
            }
            else
            {
                // Standard text message
                processedMessages.Add(new { role, content });
            }
        }
        
        requestObj["messages"] = processedMessages;
        return requestObj;
    }
    
    private object CreateMultimodalContent(string messageContent)
    {
        var contentItems = new List<object>();
        var remainingText = messageContent;
        
        int currentPosition = 0;
        
        // Process image tags
        var imageMatches = ImageBase64Regex.Matches(messageContent);
        foreach (Match match in imageMatches)
        {
            string textBefore = messageContent.Substring(currentPosition, match.Index - currentPosition);
            if (!string.IsNullOrWhiteSpace(textBefore))
            {
                contentItems.Add(new { type = "text", text = textBefore.Trim() });
            }
            
            string mimeType = match.Groups[1].Value;
            string base64Data = match.Groups[2].Value;
            
            contentItems.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{mimeType};base64,{base64Data}"
                }
            });
            
            currentPosition = match.Index + match.Length;
        }
        
        // Process file tags
        var fileMatches = FileBase64Regex.Matches(messageContent);
        foreach (Match match in fileMatches)
        {
            // Only process file tags that weren't already included in the currentPosition
            if (match.Index >= currentPosition)
            {
                string textBefore = messageContent.Substring(currentPosition, match.Index - currentPosition);
                if (!string.IsNullOrWhiteSpace(textBefore))
                {
                    contentItems.Add(new { type = "text", text = textBefore.Trim() });
                }
                
                string filename = match.Groups[1].Value;
                string fileType = match.Groups[2].Value;
                string base64Data = match.Groups[3].Value;
                
                contentItems.Add(new
                {
                    type = "file",
                    file = new
                    {
                        filename = filename,
                        // OpenAI requires the full data URL format including the 'data:' prefix
                        file_data = $"data:{fileType};base64,{base64Data}"
                    }
                });
                
                currentPosition = match.Index + match.Length;
            }
        }
        
        if (currentPosition < messageContent.Length)
        {
            string textAfter = messageContent.Substring(currentPosition);
            if (!string.IsNullOrWhiteSpace(textAfter))
            {
                contentItems.Add(new { type = "text", text = textAfter.Trim() });
            }
        }
        
        return contentItems;
    }

    protected override void AddProviderSpecificParameters(Dictionary<string, object> requestObj)
    {
        if (ShouldEnableThinking())
        {
            string reasoningEffort = GetReasoningEffort();
            
            if (ModelCode.Contains("gpt-4o")) 
            {
                requestObj["reasoning_effort"] = reasoningEffort;
                
                requestObj["response_format"] = new { type = "text" };
                
                if (!requestObj.ContainsKey("seed"))
                {
                    requestObj["seed"] = 42;
                }
            }
        }
        
        // Check if model supports vision capabilities
        if (ModelCode.Contains("gpt-4") && ModelCode.Contains("vision") || ModelCode.Contains("gpt-4o"))
        {
            // Vision models generally need JSON response for proper multimodal handling
            if (!requestObj.ContainsKey("response_format"))
            {
                requestObj["response_format"] = new { type = "text" };
            }
        }
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);

        var tokenizer = Tiktoken.ModelToEncoder.For(ModelCode);

        int inputTokens = 0;
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            inputTokens += tokenizer.Encode(msg.Content).Count;
        }

        HttpResponseMessage response;

        try
        {
            // Instead of creating the request once and reusing it, create a request factory
            // that generates a new request for each attempt
            response = await _resilienceService.CreatePluginResiliencePipeline<HttpResponseMessage>()
                .ExecuteAsync(async ct => 
                {
                    // Create a new request for each attempt
                    var newRequest = CreateRequest(requestBody);
                    return await HttpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                }, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to OpenAI: {ex.Message}");
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "OpenAI");
            yield break;
        }

        var fullResponse = new StringBuilder();
        var outputTokens = 0;

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
                outputTokens += tokenizer.Encode(text).Count;
                yield return new StreamResponse(text, inputTokens, outputTokens);
            }
        }
    }
    
    // Helper method to create a request
    private HttpRequestMessage CreateRequest(object requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };
        return request;
    }
}