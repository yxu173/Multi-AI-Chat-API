using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
        var messages = PrepareMessageList(history)
            .Select(m => new { role = m.Role, content = m.Content })
            .Take(2)
            .ToList();
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);

        // Remove parameters that aren't supported by OpenAI models
        //  CleanupUnsupportedParameters(requestObj);

        requestObj["messages"] = messages;
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
            // Process the message content to handle image and file tags
            string processedContent = msg.Content;
            bool hasImages = false;
            
            // Replace image tags with text descriptions
            var imgRegex = new System.Text.RegularExpressions.Regex(@"<image\s+type=[""']([^""']+)[""']\s+name=[""']([^""']+)[""']\s+base64=[""']([^""']+)[""']\s*>");
            processedContent = imgRegex.Replace(processedContent, match => {
                hasImages = true;
                string fileName = match.Groups[2].Value;
                return $"[Image: {fileName}]";
            });
            
            // Replace file tags with text descriptions
            var fileRegex = new System.Text.RegularExpressions.Regex(@"<file\s+type=[""']([^""']+)[""']\s+name=[""']([^""']+)[""']\s+base64=[""']([^""']+)[""']\s*>");
            processedContent = fileRegex.Replace(processedContent, match => {
                string mimeType = match.Groups[1].Value;
                string fileName = match.Groups[2].Value;
                return $"[File: {fileName} ({mimeType})]";
            });
            
            // If we have images, warn about limitations
            if (hasImages)
            {
                Console.WriteLine("Warning: OpenAI service currently doesn't properly support image content from file uploads. Images are represented as text descriptions.");
            }
            
            // Create chat message based on role
            ChatMessage chatMessage = msg.Role switch
            {
                "system" => new SystemChatMessage(processedContent),
                "user" => new UserChatMessage(processedContent),
                "assistant" => new AssistantChatMessage(processedContent),
                _ => new UserChatMessage(processedContent)
            };
            
            messages.Add(chatMessage);
        }

        return messages;
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

             
                totalTokens += encoder.CountTokens(content);


                totalTokens += message is SystemChatMessage ? 5 : 4;
            }

           
            totalTokens += 3; 

            return totalTokens;
        }
        catch
        {
            
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

                return (content.Length / 4) + 4;
            });
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
            // Fallback to approximation if Tiktoken fails
            return text.Length / 4;
        }
    }
}