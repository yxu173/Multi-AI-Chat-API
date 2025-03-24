using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : BaseAiService
{
    private const string BaseUrl = "https://api.openai.com/v1/";
    private readonly IResilienceService _resilienceService;

    public OpenAiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        IResilienceService resilienceService,
        Domain.Aggregates.Users.UserAiModelSettings? modelSettings = null,
        AiModel? aiModel = null,
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

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        // Start with a system message
        var systemMessage = "Always respond using markdown formatting";
        var messages = new List<object>();

        // Handle system instructions from history
        var messagesList = history.ToList();
        if (messagesList.Count > 0 && messagesList[0].Content.StartsWith("system:"))
        {
            // Get the system message from history
            systemMessage = messagesList[0].Content.Substring(7).Trim();
            messagesList.RemoveAt(0);
        }

        // Add system message
        messages.Add(new { role = "system", content = systemMessage });

        // Check if thinking mode is enabled using base class method
        bool enableThinking = ShouldEnableThinking();

        // If thinking is enabled, add the thinking instruction as another system message
        if (enableThinking && (ModelCode.Contains("gpt-4") || ModelCode.Contains("gpt-3.5")) &&
            !ModelCode.Contains("gpt-4o") && !ModelCode.Contains("gpt-4-turbo") && !ModelCode.Contains("gpt-4-vision"))
        {
            messages.Add(new
            {
                role = "system",
                content = "When tackling complex questions, first solve them step by step in a thinking section " +
                          "that starts with '### Thinking' and ends with '### Answer'. " +
                          "This section helps you work through the problem methodically. " +
                          "After completing your thinking process, provide a clear, concise answer in the Answer section."
            });
        }

        // Add the rest of the messages
        foreach (var msg in messagesList.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            messages.Add(new
            {
                role = msg.IsFromAi ? "assistant" : "user",
                content = msg.Content
            });
        }

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["messages"] = messages,
            ["stream"] = true
        };

        // Apply user/custom settings
        var requestWithSettings = ApplyUserSettings(requestObj);

        // OpenAI specific parameter cleanup - REMOVE UNSUPPORTED PARAMETERS
        requestWithSettings.Remove("top_k");
        requestWithSettings.Remove("topK");
        requestWithSettings.Remove("maxOutputTokens");
        requestWithSettings.Remove("max_output_tokens");
        requestWithSettings.Remove("enable_thinking"); // OpenAI doesn't support this directly
        requestWithSettings.Remove("thinking"); // Remove thinking parameter
        requestWithSettings.Remove("reasoning_effort"); // Remove reasoning_effort parameter
        requestWithSettings.Remove("enable_cot"); // Remove enable_cot parameter
        requestWithSettings.Remove("enable_reasoning"); // Remove enable_reasoning parameter
        requestWithSettings.Remove("reasoning_mode"); // Remove reasoning_mode parameter
        requestWithSettings.Remove("context_limit"); // Remove context_limit parameter
        requestWithSettings.Remove("stop_sequences"); // Remove stop_sequences parameter
        requestWithSettings.Remove("topP"); // Remove topP parameter (duplicate of top_p)
        requestWithSettings.Remove("safety_settings"); // Remove safety_settings parameter

        // Only add reasoning_effort if using GPT-4o (which supports it)
        if (enableThinking && ModelCode.Contains("gpt-4o"))
        {
            // Add reasoning_effort parameter for models that support it
            string reasoningEffort = "medium"; // Default value

            // Get from custom model parameters if available
            if (CustomModelParameters?.ReasoningEffort.HasValue == true)
            {
                // Convert the numeric reasoning effort to OpenAI's low/medium/high values
                reasoningEffort = CustomModelParameters.ReasoningEffort.Value switch
                {
                    <= 33 => "low",
                    >= 66 => "high",
                    _ => "medium"
                };

                requestWithSettings["response_format"] = new { type = "text" };
                requestWithSettings["seed"] = 42; // Consistent results
            }
        }

        // Cap tokens to OpenAI limits
        if (requestWithSettings.TryGetValue("max_tokens", out var maxTokensObj) &&
            maxTokensObj is int maxTokens && maxTokens > 16384)
        {
            requestWithSettings["max_tokens"] = 16384;
        }

        return requestWithSettings;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        // Create a local cancellation token source linked to the passed token
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var combinedToken = localCts.Token;

        var tokenizer = Tiktoken.ModelToEncoder.For(ModelCode);
        var messages = ((List<object>)((dynamic)CreateRequestBody(history))["messages"]);

        int inputTokens = 0;
        foreach (var msg in messages)
        {
            inputTokens += tokenizer.Encode(msg.ToString()).Count;
        }

        var request = CreateRequest(CreateRequestBody(history));

        var pipeline = _resilienceService.CreatePluginResiliencePipeline<HttpResponseMessage>();
        HttpResponseMessage response = null;

        try
        {
            response = await pipeline.ExecuteAsync(async ct =>
                await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct), combinedToken);
        }
        catch (OperationCanceledException)
        {
            // Just exit if canceled
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "OpenAI");
            yield break;
        }

        if (combinedToken.IsCancellationRequested)
        {
            yield break;
        }

        // Set up a task to monitor for cancellation
        var cancellationTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, combinedToken);
            }
            catch (OperationCanceledException)
            {
                // This will be triggered when cancellation is requested
                localCts.Cancel(); // Ensure local cancellation occurs
            }
        });

        await using var stream = await response.Content.ReadAsStreamAsync(combinedToken);
        using var reader = new StreamReader(stream);

        int outputTokens = 0;
        bool isThinking = false;
        var fullResponse = new StringBuilder();

        try
        {
            while (!combinedToken.IsCancellationRequested)
            {
                if (reader.EndOfStream) yield break;

                // Read with potential timeout and cancellation
                var readTask = Task.Run(() => reader.ReadLineAsync(combinedToken).AsTask());
                var completedTask = await Task.WhenAny(readTask, cancellationTask);
                if (completedTask == cancellationTask || combinedToken.IsCancellationRequested)
                {
                    yield break; // Exit immediately if cancellation is requested
                }

                var line = await readTask;

                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("data: ") && line.Trim() != "data: [DONE]")
                {
                    var json = line["data: ".Length..];
                    var chunk = JsonSerializer.Deserialize<OpenAiResponse>(json);
                    if (chunk?.choices is { Length: > 0 } choices)
                    {
                        var delta = choices[0].delta;
                        if (!string.IsNullOrEmpty(delta?.content))
                        {
                            // Check for thinking section markers
                            if (delta.content.Contains("### Thinking"))
                            {
                                isThinking = true;
                            }
                            else if (delta.content.Contains("### Answer"))
                            {
                                isThinking = false;
                            }

                            // Add to output tokens count
                            outputTokens += tokenizer.Encode(delta.content).Count;

                            // Add to full response for context tracking
                            fullResponse.Append(delta.content);

                            // Determine if this chunk is part of thinking
                            bool isCurrentChunkThinking = isThinking ||
                                                          (fullResponse.ToString().Contains("### Thinking") &&
                                                           !fullResponse.ToString().Contains("### Answer"));

                            // Yield the chunk with appropriate thinking flag
                            yield return new StreamResponse(delta.content, inputTokens, outputTokens,
                                isCurrentChunkThinking);
                        }
                    }
                }

                // Check cancellation after processing each line
                if (combinedToken.IsCancellationRequested)
                {
                    yield break;
                }
            }
        }
        finally
        {
            // Ensure cleanup
            localCts.Cancel();
        }
    }

    //  private record OpenAiMessage(string role, string content);

    private record OpenAiResponse(
        string id,
        string @object,
        int created,
        string model,
        Choice[] choices
    );

    private record Choice(Delta delta, int index, string finish_reason);

    private record Delta(string content);
}