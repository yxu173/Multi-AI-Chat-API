using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Infrastructure.Services.AiProvidersServices.Base;

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
        AiModel? aiModel = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel)
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
        var messages = new List<OpenAiMessage>
        {
            new("system", "Always respond using markdown formatting")
        };

        messages.AddRange(history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new OpenAiMessage(m.IsFromAi ? "assistant" : "user", m.Content)));

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = ModelCode,
            ["messages"] = messages,
            ["stream"] = true
        };


        var requestWithSettings = ApplyUserSettings(requestObj);

        requestWithSettings.Remove("top_k");
        requestWithSettings.Remove("topK");
        requestWithSettings.Remove("maxOutputTokens");
        requestWithSettings.Remove("max_output_tokens");

        if (requestWithSettings.TryGetValue("max_tokens", out var maxTokensObj) &&
            maxTokensObj is int maxTokens && maxTokens > 16384)
        {
            requestWithSettings["max_tokens"] = 16384;
        }

        return requestWithSettings;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        CancellationToken cancellationToken)
    {
        // Create a local cancellation token source linked to the passed token
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var combinedToken = localCts.Token;

        var tokenizer = Tiktoken.ModelToEncoder.For(ModelCode);
        var messages = ((List<OpenAiMessage>)((dynamic)CreateRequestBody(history))["messages"]);

        int inputTokens = 0;
        foreach (var msg in messages)
        {
            inputTokens += tokenizer.Encode(msg.content).Count;
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
        catch (Exception ex)
        {
            //_logger.LogError(ex, "Failed to initiate streaming after retries.");
            throw;
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
                            outputTokens += tokenizer.Encode(delta.content).Count;
                            yield return new StreamResponse(delta.content, inputTokens, outputTokens);
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

    private record OpenAiMessage(string role, string content);

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