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
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
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
            messages.Add(("system",
                "When solving complex problems, please show your detailed step-by-step thinking process marked as '### Thinking:' before providing the final answer marked as '### Answer:'. Analyze all relevant aspects of the problem thoroughly."));
        }

        string lastRole = messages.Count > 0 ? "system" : null;
        MessageDto? pendingMsg = null;
        foreach (var msg in history.Where(m => !string.IsNullOrEmpty(m.Content)))
        {
            // Process content to handle image and file tags
            string processedContent = msg.Content;
            
            // Replace image tags with text descriptions
            var imgRegex = new System.Text.RegularExpressions.Regex(@"<image\s+type=[""']([^""']+)[""']\s+name=[""']([^""']+)[""']\s+base64=[""']([^""']+)[""']\s*>");
            processedContent = imgRegex.Replace(processedContent, match => {
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
            
            string currentRole = msg.IsFromAi ? "assistant" : "user";
            if (currentRole == lastRole && pendingMsg != null)
            {
                pendingMsg = new MessageDto(
                    pendingMsg.Content + "\n\n" + processedContent.Trim(),
                    pendingMsg.IsFromAi,
                    pendingMsg.MessageId
                );
            }
            else
            {
                if (pendingMsg != null)
                {
                    messages.Add((lastRole, pendingMsg.Content.Trim()));
                }

                pendingMsg = new MessageDto(
                    processedContent.Trim(),
                    msg.IsFromAi,
                    msg.MessageId
                );
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

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(CreateRequestBody(history));
        using var response =
            await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "DeepSeek");
            yield break;
        }

        var fullResponse = new StringBuilder();
        var contentBuffer = new List<string>();
        DeepSeekUsage? finalUsage = null;

        await foreach (var json in ReadStreamAsync(response, cancellationToken))
        {
            var chunk = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (chunk.TryGetValue("choices", out var choicesElement) &&
                choicesElement.ValueKind == JsonValueKind.Array)
            {
                var choice = choicesElement[0];

                if (choice.TryGetProperty("finish_reason", out var finishReason) &&
                    finishReason.ValueKind != JsonValueKind.Null &&
                    chunk.TryGetValue("usage", out var usageElement))
                {
                    finalUsage = JsonSerializer.Deserialize<DeepSeekUsage>(usageElement.GetRawText());

                    foreach (var bufferedContent in contentBuffer)
                    {
                        yield return new StreamResponse(
                            bufferedContent,
                            finalUsage.prompt_tokens,
                            finalUsage.completion_tokens);
                    }

                    contentBuffer.Clear();
                }

                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind != JsonValueKind.Null)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        fullResponse.Append(text);

                        if (finalUsage != null)
                        {
                            yield return new StreamResponse(
                                text,
                                finalUsage.prompt_tokens,
                                finalUsage.completion_tokens);
                        }
                        else
                        {
                            contentBuffer.Add(text);
                        }
                    }
                }
            }
        }

        if (finalUsage != null && contentBuffer.Count > 0)
        {
            foreach (var bufferedContent in contentBuffer)
            {
                yield return new StreamResponse(
                    bufferedContent,
                    finalUsage.prompt_tokens,
                    finalUsage.completion_tokens);
            }
        }
    }

    private record DeepSeekUsage(int prompt_tokens, int completion_tokens, int total_tokens);
}