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
        return base.PrepareMessageList(history);
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var baseRequestBody = (Dictionary<string, object>)base.CreateRequestBody(history);

        var messagesList = base.PrepareMessageList(history);

        var processedMessages = new List<object>();
        foreach (var (role, rawContent) in messagesList)
        {
            if (role == "user")
            {
                var contentParts = ParseMultimodalContent(rawContent);
                bool isMultimodal = contentParts.Any(p => p is ImagePart || p is FilePart);

                if (isMultimodal)
                {
                    var openAiContentItems = new List<object>();
                    foreach (var part in contentParts)
                    {
                        switch (part)
                        {
                            case TextPart textPart:
                                openAiContentItems.Add(new { type = "text", text = textPart.Text });
                                break;
                            case ImagePart imagePart:
                                openAiContentItems.Add(new {
                                    type = "image_url",
                                    image_url = new { url = $"data:{imagePart.MimeType};base64,{imagePart.Base64Data}" }
                                });
                                break;
                            case FilePart filePart:
                                string fileDataUrl = $"data:{filePart.MimeType};base64,{filePart.Base64Data}";
                                openAiContentItems.Add(new {
                                    type = "file",
                                    file = new {
                                        filename = filePart.FileName,
                                        file_data = fileDataUrl
                                    }
                                });
                                break;
                            default:
                                openAiContentItems.Add(new { type = "text", text = "[Unsupported content type]" });
                                break;
                        }
                    }
                    if (openAiContentItems.Any())
                    {
                        processedMessages.Add(new { role, content = openAiContentItems });
                    }
                }
                else if (!string.IsNullOrWhiteSpace(rawContent))
                {
                    processedMessages.Add(new { role, content = rawContent });
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(rawContent))
                {
                    processedMessages.Add(new { role, content = rawContent });
                }
            }
        }

        baseRequestBody["messages"] = processedMessages;

        if (ShouldEnableThinking())
        {
            var parametersToRemove = new List<string> { "temperature", "top_p", "top_k", "frequency_penalty", "presence_penalty", "seed", "response_format" };

            foreach (var param in parametersToRemove)
            {
                if (baseRequestBody.ContainsKey(param))
                {
                    baseRequestBody.Remove(param);
                    Console.WriteLine($"OpenAI Specific: Removed {param} due to thinking mode override.");
                }
            }
        }

        return baseRequestBody;
    }

    protected override void AddProviderSpecificParameters(Dictionary<string, object> requestObj)
    {
        if (ShouldEnableThinking())
        {
            requestObj["response_format"] = new { type = "text" };
            if (!requestObj.ContainsKey("seed"))
            {
                requestObj["seed"] = 42;
            }
        }

        bool isVisionModel = ModelCode.Contains("vision") || ModelCode.Contains("gpt-4o");
        if (isVisionModel)
        {
            if (!requestObj.ContainsKey("response_format"))
            {
                // requestObj["response_format"] = new { type = "json_object" };
            }
        }
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);

        int inputTokens = 0;
        var tokenizer = Tiktoken.ModelToEncoder.For(ModelCode);

        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            inputTokens += tokenizer?.Encode(msg.Content).Count ?? msg.Content.Length / 4;
        }

        HttpResponseMessage response;

        try
        {
            response = await _resilienceService.CreatePluginResiliencePipeline<HttpResponseMessage>()
                .ExecuteAsync(async ct =>
                {
                    var request = CreateRequest(requestBody);
                    return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to OpenAI via ResiliencePipeline: {ex.Message}");
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
            if (cancellationToken.IsCancellationRequested) break;

            string? text = null;
            try
            {
                using var chunkDoc = JsonDocument.Parse(json);
                if (chunkDoc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        text = content.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error processing OpenAI response chunk: {ex.Message} - Chunk: {json}");
                continue;
            }

            if (!string.IsNullOrEmpty(text))
            {
                fullResponse.Append(text);
                outputTokens += tokenizer?.Encode(text).Count ?? text.Length / 4;
                yield return new StreamResponse(text, inputTokens, outputTokens);
            }
        }
    }
}