using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices;

public class GeminiService : BaseAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public GeminiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        Domain.Aggregates.Users.UserAiModelSettings? modelSettings = null,
        AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
    }

    protected override void ConfigureHttpClient()
    {
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        // Handle system message if present
        var messagesList = history.ToList();
        string systemPrompt = "";
        
        if (messagesList.Count > 0 && messagesList[0].Content.StartsWith("system:"))
        {
            systemPrompt = messagesList[0].Content.Substring(7).Trim();
            messagesList.RemoveAt(0);
        }
        else
        {
            systemPrompt = GetSystemMessage();
        }

        // Check if thinking mode is enabled using base class method
        bool enableThinking = ShouldEnableThinking();

        // For Gemini, we need to modify the messages to include the system prompt and thinking instructions
        var modifiedMessages = new List<MessageDto>();
        
        // Add the system prompt as a user message for Gemini (since it doesn't have a system role)
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            string userPrompt = systemPrompt;
            
            // If thinking is enabled, add thinking instructions
            if (enableThinking && AiModel?.SupportsThinking == true)
            {
                userPrompt += "\n\nWhen solving complex problems, first show your step-by-step thinking process " +
                    "marked as '### Thinking:' before giving the final answer marked as '### Answer:'.";
            }
            
            modifiedMessages.Add(new MessageDto(userPrompt, false, Guid.NewGuid()));
        }
        
        // Add the most recent messages (Gemini has context limitations)
        modifiedMessages.AddRange(messagesList.TakeLast(10));
        
        var contents = modifiedMessages.Select(m => new
        {
            role = m.IsFromAi ? "model" : "user",
            parts = new[] { new { text = m.Content } }
        }).ToArray();

        var generationConfig = new Dictionary<string, object>();

        // Set default parameters
        if (AiModel?.MaxOutputTokens.HasValue == true)
        {
            generationConfig["maxOutputTokens"] = AiModel.MaxOutputTokens.Value;
        }

        // Apply user settings
        var userSettings = ApplyUserSettings(generationConfig);

        // Create the request object structure required by Gemini
        var jsonObject = new
        {
            contents,
            generationConfig = userSettings
        };

        // Gemini specific parameter cleanup - REMOVE UNSUPPORTED PARAMETERS
        if (userSettings.ContainsKey("frequency_penalty"))
            userSettings.Remove("frequency_penalty");
            
        if (userSettings.ContainsKey("presence_penalty"))
            userSettings.Remove("presence_penalty");
            
        if (userSettings.ContainsKey("max_tokens"))
            userSettings.Remove("max_tokens");
            
        if (userSettings.ContainsKey("thinking"))
            userSettings.Remove("thinking");
            
        if (userSettings.ContainsKey("enable_thinking"))
            userSettings.Remove("enable_thinking");
            
        if (userSettings.ContainsKey("enable_cot"))
            userSettings.Remove("enable_cot");
            
        if (userSettings.ContainsKey("enable_reasoning"))
            userSettings.Remove("enable_reasoning");
            
        if (userSettings.ContainsKey("reasoning_mode"))
            userSettings.Remove("reasoning_mode");
            
        if (userSettings.ContainsKey("context_limit"))
            userSettings.Remove("context_limit");
            
        if (userSettings.ContainsKey("stop_sequences"))
            userSettings.Remove("stop_sequences");
            
        if (userSettings.ContainsKey("reasoning_effort"))
            userSettings.Remove("reasoning_effort");
            
        if (userSettings.ContainsKey("safety_settings"))
            userSettings.Remove("safety_settings");

        // Convert top_p to topP for Gemini API
        if (userSettings.ContainsKey("top_p"))
        {
            double topP = (double)userSettings["top_p"];
            userSettings["topP"] = topP;
            userSettings.Remove("top_p");
        }

        // Convert top_k to topK for Gemini API
        if (userSettings.ContainsKey("top_k"))
        {
            int topK = (int)userSettings["top_k"];
            userSettings["topK"] = topK;
            userSettings.Remove("top_k");
        }

        return jsonObject;
    }

    protected override HttpRequestMessage CreateRequest(object requestBody)
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

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(CreateRequestBody(history));
        using var response =
            await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Gemini");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            var responseArray =
                await JsonSerializer.DeserializeAsync<JsonElement?>(stream, cancellationToken: cancellationToken);

            if (responseArray.HasValue && responseArray.Value.ValueKind != JsonValueKind.Null)
            {
                var fullResponse = new StringBuilder();
                bool isThinking = false;
                
                foreach (var root in responseArray.Value.EnumerateArray())
                {
                    // Check for cancellation within the loop
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("Cancellation requested, stopping stream.");
                        break;
                    }

                    string? text = null;
                    int promptTokens = 0, outputTokens = 0;

                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0 &&
                            parts[0].TryGetProperty("text", out var textElement))
                        {
                            text = textElement.GetString();
                        }
                    }

                    if (root.TryGetProperty("usageMetadata", out var usageMetadata))
                    {
                        promptTokens = usageMetadata.GetProperty("promptTokenCount").GetInt32();
                        outputTokens = usageMetadata.TryGetProperty("candidatesTokenCount", out var outputTokenElement)
                            ? outputTokenElement.GetInt32()
                            : usageMetadata.GetProperty("totalTokenCount").GetInt32() - promptTokens;
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        // Check for thinking section markers
                        if (text.Contains("### Thinking:"))
                        {
                            isThinking = true;
                        }
                        else if (text.Contains("### Answer:"))
                        {
                            isThinking = false;
                        }
                        
                        // Add to full response for context tracking
                        fullResponse.Append(text);
                        
                        // Determine if this chunk is part of thinking
                        bool isCurrentChunkThinking = isThinking || 
                                                (fullResponse.ToString().Contains("### Thinking:") && 
                                                !fullResponse.ToString().Contains("### Answer:"));
                        
                        yield return new StreamResponse(text, promptTokens, outputTokens, isCurrentChunkThinking);
                    }
                }
            }
        }
        finally
        {
            // Ensure the stream is disposed even if cancellation occurs
            if (stream != null)
            {
                await stream.DisposeAsync();
            }

            if (response != null)
            {
                response.Dispose();
            }
        }
    }
}