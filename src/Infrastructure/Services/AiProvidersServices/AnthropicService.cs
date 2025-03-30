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
using System.Collections.Generic;
using System.Linq;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";

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
        var requestObj = (Dictionary<string, object>)base.CreateRequestBody(history);

        var baseMessages = base.PrepareMessageList(history);

        var systemMessageContent = baseMessages
            .Where(m => m.Role == "system")
            .Select(m => m.Content)
            .Aggregate(string.Empty, (current, next) => string.IsNullOrEmpty(current) ? next : $"{current}\n\n{next}")
            .Trim();
        if (!string.IsNullOrEmpty(systemMessageContent))
        {
            requestObj["system"] = systemMessageContent;
        }

        var otherMessages = baseMessages
            .Where(m => m.Role != "system")
            .Select(m => CreateAnthropicContentMessage(m.Role, m.Content))
            .Where(m => m != null)
            .ToList();

        if (!requestObj.ContainsKey("max_tokens"))
        {
            requestObj["max_tokens"] = 20000;
        }

        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
        if (ShouldEnableThinking())
        {
            requestObj.Remove("top_k");
            requestObj.Remove("top_p");
        }

        requestObj["messages"] = otherMessages;
        return requestObj;
    }

    private object? CreateAnthropicContentMessage(string role, string content)
    {
        string anthropicRole = role == "assistant" ? "assistant" : "user";
        var contentItems = new List<object>();

        if (anthropicRole == "user")
        {
            var parsedParts = ParseMultimodalContent(content);
            bool isMultimodal = parsedParts.Count > 1 || parsedParts.Any(p => p is ImagePart || p is FilePart);

            if (isMultimodal)
            {
                foreach (var part in parsedParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            contentItems.Add(new { type = "text", text = textPart.Text });
                            break;
                        case ImagePart imagePart:
                            if (IsValidAnthropicImageType(imagePart.MimeType, out string normalizedMediaType))
                            {
                                contentItems.Add(new {
                                    type = "image",
                                    source = new {
                                        type = "base64",
                                        media_type = normalizedMediaType,
                                        data = imagePart.Base64Data
                                    }
                                });
                            }
                            else
                            {
                                contentItems.Add(new { type = "text", text = $"[Image: Unsupported format '{imagePart.MimeType}']" });
                            }
                            break;
                        case FilePart filePart:
                            contentItems.Add(new {
                                type = "document",
                                source = new {
                                    type = "base64",
                                    media_type = filePart.MimeType,
                                    data = filePart.Base64Data
                                }
                            });
                            break;
                        default:
                            contentItems.Add(new { type = "text", text = "[Unsupported content type]" });
                            break;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(content))
            {
                return new { role = anthropicRole, content = content.Trim() };
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                return new { role = anthropicRole, content = content.Trim() };
            }
        }

        if (contentItems.Any())
        {
            return new { role = anthropicRole, content = contentItems.ToArray() };
        }

        return null;
    }

    private bool IsValidAnthropicImageType(string mediaType, out string normalizedMediaType)
    {
        string mediaTypeLower = mediaType.ToLowerInvariant().Trim();

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
            Console.WriteLine("Anthropic Specific: Thinking mode enabled (Temp=1.0). Ensure 'thinking' parameter is current.");
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

        await foreach (var json in ReadStreamAsync(response, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            StreamResponse? responseToSend = null;
            bool shouldBreak = false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement) || typeElement.GetString() == null) continue;
                var type = typeElement.GetString()!;

                switch (type)
                {
                    case "message_start":
                        if (root.TryGetProperty("message", out var messageElement) &&
                            messageElement.TryGetProperty("usage", out var usageElement) &&
                            usageElement.TryGetProperty("input_tokens", out var inputTokensElement) &&
                            inputTokensElement.TryGetInt32(out var iTokens))
                        {
                            inputTokens = iTokens;
                        }
                        break;

                    case "content_block_delta":
                        if (root.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("text", out var textElement) &&
                            textElement.ValueKind == JsonValueKind.String)
                        {
                            var text = textElement.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                fullResponse.Append(text);
                                estimatedOutputTokens = outputTokens > 0 ? outputTokens : Math.Max(1, fullResponse.Length / 4);
                                responseToSend = new StreamResponse(text, inputTokens, estimatedOutputTokens);
                            }
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("usage", out var deltaUsage) &&
                            deltaUsage.TryGetProperty("output_tokens", out var outputTokensElement) &&
                            outputTokensElement.TryGetInt32(out var oTokens))
                        {
                            outputTokens = oTokens;
                        }
                        break;

                    case "ping":
                        break;

                    case "error":
                        if (root.TryGetProperty("error", out var errorDetails))
                        {
                            Console.WriteLine($"Anthropic Stream Error: {errorDetails.ToString()}");
                        }
                        shouldBreak = true;
                        break;

                    case "message_stop":
                        if (outputTokens > 0 && estimatedOutputTokens != outputTokens)
                        {
                            responseToSend = new StreamResponse("", inputTokens, outputTokens);
                        }
                        shouldBreak = true;
                        break;

                    default:
                        Console.WriteLine($"Anthropic Stream: Unknown event type '{type}'");
                        break;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error processing Anthropic response chunk: {ex.Message} - Chunk: {json}");
                continue;
            }

            if (responseToSend != null)
            {
                yield return responseToSend;
            }

            if (shouldBreak)
            {
                break;
            }
        }
    }
}