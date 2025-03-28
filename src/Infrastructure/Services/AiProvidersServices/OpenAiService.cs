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
using OpenAI;
using OpenAI.Chat;
using Tiktoken;

public class OpenAiService : BaseAiService
{
    private const string BaseUrl = "https://api.openai.com/v1/";
    private readonly ChatClient _chatClient;
    private int _inputTokens;
    private int _outputTokens;

    // Properties to expose token counts
    public int InputTokens => _inputTokens;
    public int OutputTokens => _outputTokens;
    public int TotalTokens => _inputTokens + _outputTokens;

    public OpenAiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
        _inputTokens = 0;
        _outputTokens = 0;

        // Initialize the official OpenAI client
        _chatClient = new ChatClient(modelCode, apiKey);
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
            "low" =>
                "For simpler questions, provide a brief explanation under '### Thinking:' then give your answer under '### Answer:'. Keep your thinking concise.",

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
        var messages = PrepareMessageList(history);
        // Only get a couple messages for testing, we'll handle the full implementation below
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);

        var formattedMessages = new List<object>();

        foreach (var msg in messages)
        {
            // Check if this is a user message that might contain images
            if (msg.Role == "user" && (msg.Content.Contains("<image:") || msg.Content.Contains("<file:")))
            {
                formattedMessages.Add(CreateMultiModalMessage(msg.Role, msg.Content));
            }
            else
            {
                // Regular text-only message
                formattedMessages.Add(new
                {
                    role = msg.Role,
                    content = msg.Content
                });
            }
        }

        requestObj["messages"] = formattedMessages;
        return requestObj;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = ConvertMessagesToSdkFormat(history);
        var options = GetChatCompletionOptions();


        _inputTokens = CalculateInputTokens(messages);


        var fullResponse = new StringBuilder();
        _outputTokens = 0;
        var inThinkingSection = false;


        var streamingResult = _chatClient.CompleteChatStreamingAsync(messages, options);

        await foreach (var update in streamingResult.WithCancellation(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;


            if (update.ContentUpdate.Count > 0)
            {
                foreach (var contentPart in update.ContentUpdate)
                {
                    
                    string text = contentPart?.Text ?? string.Empty;
                    if (string.IsNullOrEmpty(text)) continue;

                   
                    fullResponse.Append(text);

                    
                    var chunkTokens = CountTokens(text);
                    _outputTokens += chunkTokens;

                    yield return new StreamResponse(
                        text,
                        _inputTokens,
                        _outputTokens,
                        inThinkingSection
                    );
                }
            }
        }
    }

    private List<ChatMessage> ConvertMessagesToSdkFormat(IEnumerable<MessageDto> history)
    {
        var messages = new List<ChatMessage>();
        foreach (var msg in PrepareMessageList(history))
        {
            string role = msg.Role;
            string content = msg.Content;

            // Regular text-only message - the SDK will handle multimodal in StreamResponseAsync
            ChatMessage chatMessage = role switch
            {
                "system" => new SystemChatMessage(content),
                "user" => new UserChatMessage(content),
                "assistant" => new AssistantChatMessage(content),
                _ => new UserChatMessage(content)
            };
            messages.Add(chatMessage);
        }
        return messages;
    }

  private object CreateMultiModalMessage(string role, string content)
{
    var contentItems = new List<object>();
    int currentPosition = 0;

    while (currentPosition < content.Length)
    {
        int imageStart = content.IndexOf("<image:", currentPosition);
        int fileStart = content.IndexOf("<file:", currentPosition);
        int imageBase64Start = content.IndexOf("<image-base64:", currentPosition);
        
        int nextTagStart = -1;
        string tagType = "";
        
        if (imageStart >= 0 && (fileStart < 0 || imageStart < fileStart) && (imageBase64Start < 0 || imageStart < imageBase64Start))
        {
            nextTagStart = imageStart;
            tagType = "image";
        }
        else if (fileStart >= 0 && (imageBase64Start < 0 || fileStart < imageBase64Start))
        {
            nextTagStart = fileStart;
            tagType = "file";
        }
        else if (imageBase64Start >= 0)
        {
            nextTagStart = imageBase64Start;
            tagType = "image-base64";
        }
        
        if (nextTagStart < 0)
        {
            if (currentPosition < content.Length)
            {
                string textContent = content.Substring(currentPosition).Trim();
                if (!string.IsNullOrEmpty(textContent))
                {
                    contentItems.Add(new { type = "text", text = textContent });
                }
            }
            break;
        }
        
        if (nextTagStart > currentPosition)
        {
            string textBefore = content.Substring(currentPosition, nextTagStart - currentPosition).Trim();
            if (!string.IsNullOrEmpty(textBefore))
            {
                contentItems.Add(new { type = "text", text = textBefore });
            }
        }
        
        int closeTagIndex = content.IndexOf(">", nextTagStart);
        if (closeTagIndex < 0)
        {
            string remainingContent = content.Substring(currentPosition).Trim();
            if (!string.IsNullOrEmpty(remainingContent))
            {
                contentItems.Add(new { type = "text", text = remainingContent });
            }
            break;
        }
        
        string tagContent = content.Substring(nextTagStart, closeTagIndex - nextTagStart + 1);
        
        if (tagType == "image")
        {
            string url = tagContent.Substring(7, tagContent.Length - 8);
            if (ValidateAndFixUrl(ref url))
            {
                contentItems.Add(new 
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = url
                    }
                });
            }
            else 
            {
                contentItems.Add(new { type = "text", text = "[Image: URL not supported]" });
            }
        }
        else if (tagType == "image-base64")
        {
            string base64Content = tagContent.Substring(13, tagContent.Length - 14);
            int separatorIndex = base64Content.IndexOf(';');
            if (separatorIndex > 0)
            {
                string mediaType = base64Content.Substring(0, separatorIndex);
                string base64Data = base64Content.Substring(separatorIndex + 1);
                
                // Remove 'base64,' prefix if present
                if (base64Data.StartsWith("base64,"))
                {
                    base64Data = base64Data.Substring(7);
                }
                
                contentItems.Add(new 
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:{mediaType};base64,{base64Data}"
                    }
                });
            }
            else
            {
                contentItems.Add(new { type = "text", text = "[Image data format error]" });
            }
        }
        else if (tagType == "file")
        {
            string url = tagContent.Substring(6, tagContent.Length - 7);
            contentItems.Add(new 
            {
                type = "text",
                text = $"[File attachment: {url}]"
            });
        }
        
        currentPosition = closeTagIndex + 1;
    }

    if (contentItems.Count == 0)
    {
        contentItems.Add(new { type = "text", text = "" });
    }

    return new
    {
        role = role,
        content = contentItems
    };
}

    private bool ValidateAndFixUrl(ref string url)
    {
        // Trim the URL
        url = url.Trim();
        
        // Check if URL is valid
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }
        
        // Try to create a Uri object to validate
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
        {
            return false;
        }
        
        // Check if the URL uses HTTPS
        if (result.Scheme != "https")
        {
            return false; // OpenAI only accepts HTTPS URLs for security
        }
        
        // Update the URL to the validated one
        url = result.ToString();
        return true;
    }

    private ChatCompletionOptions GetChatCompletionOptions()
    {
        var options = new ChatCompletionOptions();
        var parameters = GetModelParameters();

        
        // Map standard parameters
        MapParameterIfExists<double, float>("temperature", parameters, value => options.Temperature = value);
        MapParameterIfExists<double, float>("top_p", parameters, value => options.TopP = value);
        MapParameterIfExists<double, float>("frequency_penalty", parameters, value => options.FrequencyPenalty = value);
        MapParameterIfExists<double, float>("presence_penalty", parameters, value => options.PresencePenalty = value);

        // Handle max tokens
        if (parameters.TryGetValue("max_tokens", out var mt) && mt is int maxTokens)
        {
            try
            {
                var property = typeof(ChatCompletionOptions).GetProperty("MaxTokens") ??
                               typeof(ChatCompletionOptions).GetProperty("MaxCompletionTokens");

                property?.SetValue(options, maxTokens);
            }
            catch
            {
                Console.WriteLine("Warning: Could not set max tokens parameter");
            }
        }

      
        if (ShouldEnableThinking())
        {
            options.Temperature = null; 
            options.TopP = null; 
            if (ModelCode.Contains("o3"))
            {
                var reasoningEffort = GetReasoningEffort();

                try
                {
                    var reasoningOptionsType =
                        typeof(ChatCompletionOptions).Assembly.GetType("OpenAI.Chat.ResponseReasoningOptions");
                    var reasoningEffortLevelType =
                        typeof(ChatCompletionOptions).Assembly.GetType("OpenAI.Chat.ResponseReasoningEffortLevel");

                    if (reasoningOptionsType != null && reasoningEffortLevelType != null)
                    {
                       
                        var reasoningOptions = Activator.CreateInstance(reasoningOptionsType);

                        
                        var effortLevelProperty = reasoningOptionsType.GetProperty("ReasoningEffortLevel");
                        var effortLevelValue = Enum.Parse(reasoningEffortLevelType, reasoningEffort switch
                        {
                            "low" => "Low",
                            "high" => "High",
                            _ => "Medium"
                        });

                        effortLevelProperty?.SetValue(reasoningOptions, effortLevelValue);

                        // Set the reasoning options on the chat options
                        var reasoningProperty = typeof(ChatCompletionOptions).GetProperty("ReasoningOptions");
                        reasoningProperty?.SetValue(options, reasoningOptions);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not set reasoning options: {ex.Message}");
                }
            }
        }

        return options;
    }

    private void MapParameterIfExists<TSource, TDest>(string paramName, Dictionary<string, object> parameters,
        Action<TDest> setter)
        where TSource : IConvertible
    {
        if (parameters.TryGetValue(paramName, out var value) && value is TSource sourceValue)
        {
            setter((TDest)Convert.ChangeType(sourceValue, typeof(TDest)));
        }
    }

    private int CalculateInputTokens(List<ChatMessage> messages)
    {
        try
        {
            var encoder = ModelToEncoder.For(ModelCode);
            int totalTokens = 0;

            foreach (var message in messages)
            {
                string content = "";
                if (message is SystemChatMessage systemMsg)
                {
                    content = systemMsg.Content.ToString();
                }
                else if (message is UserChatMessage userMsg)
                {
                    content = userMsg.Content.ToString();
                }
                else if (message is AssistantChatMessage assistantMsg)
                {
                    content = assistantMsg.Content.ToString();
                }

                // Count tokens for the content
                totalTokens += encoder.CountTokens(content);

                // Add role tokens (4 for user/assistant, 5 for system)
                totalTokens += message is SystemChatMessage ? 5 : 4;
            }

            // Add conversation format tokens (3 for the entire conversation)
            totalTokens += 3;

            return totalTokens;
        }
        catch
        {
            // Fallback to a more conservative approximation
            return messages.Sum(m =>
            {
                string content = "";
                if (m is SystemChatMessage systemMsg)
                {
                    content = systemMsg.Content.ToString();
                }
                else if (m is UserChatMessage userMsg)
                {
                    content = userMsg.Content.ToString();
                }
                else if (m is AssistantChatMessage assistantMsg)
                {
                    content = assistantMsg.Content.ToString();
                }

                // More conservative approximation: 1 token per word + role tokens
                var wordCount = content.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                return wordCount + (m is SystemChatMessage ? 5 : 4);
            }) + 3; // Add conversation format tokens
        }
    }

    private int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        try
        {
            var encoder = ModelToEncoder.For(ModelCode);
            return encoder.CountTokens(text);
        }
        catch
        {
            // More conservative fallback: 1 token per word
            return text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}