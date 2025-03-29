using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;
using System.IO;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultThinkingBudget = 16000;

    public AnthropicService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    protected override string GetEndpointPath() => "messages";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history);
        var systemMessage = messages.FirstOrDefault(m => m.Role == "system").Content;
        var otherMessages = messages.Where(m => m.Role != "system")
            .Select(m => CreateContentMessage(m))
            .ToList();
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);

        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
        if (ShouldEnableThinking())
        {
            requestObj.Remove("top_k");
            requestObj.Remove("top_p");
        }

        if (!requestObj.ContainsKey("max_tokens")) requestObj["max_tokens"] = 20000;
        if (!string.IsNullOrEmpty(systemMessage)) requestObj["system"] = systemMessage;
        requestObj["messages"] = otherMessages;
        return requestObj;
    }

    private object CreateContentMessage(ValueTuple<string, string> message)
    {
        string role = message.Item1;
        string content = message.Item2;
        var contentItems = new List<object>();

        if (role == "user")
        {
            var regex = new System.Text.RegularExpressions.Regex(@"<(image|file)-base64:([^;>]+);base64,([^>]+)>");
            var matches = regex.Matches(content);
            int lastIndex = 0;

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    string textBefore = content.Substring(lastIndex, match.Index - lastIndex).Trim();
                    if (!string.IsNullOrEmpty(textBefore))
                    {
                        contentItems.Add(new { type = "text", text = textBefore });
                    }
                }

                string tagType = match.Groups[1].Value;
                string metaData = match.Groups[2].Value;
                string base64Data = match.Groups[3].Value;

                if (tagType == "image")
                {
                    string mediaType = metaData;
                    if (IsValidAnthropicImageType(mediaType, out string normalizedMediaType))
                    {
                        contentItems.Add(new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = normalizedMediaType,
                                data = base64Data
                            }
                        });
                    }
                    else
                    {
                        contentItems.Add(new { type = "text", text = $"[Image: Unsupported format '{mediaType}']" });
                    }
                }
                else if (tagType == "file")
                {
                    string fileName = "unknown";
                    string fileContentType = "unknown";
                    var metaParts = metaData.Split(new[] { ':' }, 2);
                    if (metaParts.Length > 0) fileName = metaParts[0];
                    if (metaParts.Length > 1) fileContentType = metaParts[1];

                    if (fileContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        contentItems.Add(new
                        {
                            type = "document",
                            source = new
                            {
                                type = "base64",
                                media_type = fileContentType,
                                data = base64Data
                            }
                        });
                    }
                    else
                    {
                        contentItems.Add(new
                            { type = "text", text = $"[Uploaded File: {fileName} ({fileContentType})]" });
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < content.Length)
            {
                string textAfter = content.Substring(lastIndex).Trim();
                if (!string.IsNullOrEmpty(textAfter))
                {
                    contentItems.Add(new { type = "text", text = textAfter });
                }
            }

            if (matches.Count == 0 && contentItems.Count == 0 && !string.IsNullOrEmpty(content))
            {
                contentItems.Add(new { type = "text", text = content });
            }
            else if (matches.Count > 0 && contentItems.Count == 0)
            {
                contentItems.Add(new { type = "text", text = "[Content included attachments only]" });
            }
        }
        else
        {
            contentItems.Add(new { type = "text", text = content });
        }

        if (contentItems.Count == 0 && !string.IsNullOrEmpty(content))
        {
            contentItems.Add(new { type = "text", text = content });
        }

        return new { role = role == "assistant" ? "assistant" : "user", content = contentItems.ToArray() };
    }

    private bool IsValidAnthropicImageType(string mediaType, out string normalizedMediaType)
    {
        string mediaTypeLower = mediaType.ToLowerInvariant();

        if (mediaTypeLower == "image/jpeg" || mediaTypeLower == "image/jpg")
        {
            normalizedMediaType = "image/jpeg";
            return true;
        }

        if (mediaTypeLower == "image/png")
        {
            normalizedMediaType = "image/png";
            return true;
        }

        if (mediaTypeLower == "image/gif")
        {
            normalizedMediaType = "image/gif";
            return true;
        }

        if (mediaTypeLower == "image/webp")
        {
            normalizedMediaType = "image/webp";
            return true;
        }

        normalizedMediaType = string.Empty;
        return false;
    }

    protected override void AddProviderSpecificParameters(Dictionary<string, object> requestObj)
    {
        if (ShouldEnableThinking())
        {
            requestObj["temperature"] = 1.0;
            requestObj["thinking"] = new { type = "enabled", budget_tokens = DefaultThinkingBudget };
        }
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        var request = CreateRequest(requestBody);

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to Anthropic: {ex.Message}");
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "Anthropic");
            yield break;
        }

        var fullResponse = new StringBuilder();
        int inputTokens = 0;
        int outputTokens = 0;
        int estimatedOutputTokens = 0;
        HashSet<string> sentChunks = new HashSet<string>();

        await foreach (var json in ReadStreamAsync(response, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "message_start":
                    if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.TryGetProperty("usage", out var usageElement) &&
                        usageElement.TryGetProperty("input_tokens", out var inputTokensElement))
                    {
                        inputTokens = inputTokensElement.GetInt32();
                    }

                    break;

                case "content_block_delta":
                    if (doc.RootElement.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            fullResponse.Append(text);
                            estimatedOutputTokens = Math.Max(1, fullResponse.Length / 4);

                            if (!sentChunks.Contains(text))
                            {
                                sentChunks.Add(text);
                                yield return new StreamResponse(text, inputTokens, estimatedOutputTokens);
                            }
                        }
                    }

                    break;

                case "message_delta":
                    if (doc.RootElement.TryGetProperty("usage", out var deltaUsage) &&
                        deltaUsage.TryGetProperty("output_tokens", out var outputTokensElement))
                    {
                        outputTokens = outputTokensElement.GetInt32();
                        estimatedOutputTokens = outputTokens;
                    }

                    break;
            }
        }
    }
}