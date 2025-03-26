using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;

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
        HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
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

        if (content.Contains("![") || content.Contains("<image") || content.Contains("<file"))
        {
            return new
            {
                role = role,
                content = ParseContentWithMedia(content)
            };
        }

        return new
        {
            role = role,
            content = content
        };
    }

    private object[] ParseContentWithMedia(string content)
    {
        List<object> contentParts = new List<object>();
        int currentPosition = 0;
        StringBuilder currentText = new StringBuilder();

        while (currentPosition < content.Length)
        {
            int imageStart = content.IndexOf("<image", currentPosition);
            int fileStart = content.IndexOf("<file", currentPosition);
            
            int nextTagStart = -1;
            bool isImageTag = false;
            
            // Determine which tag comes first
            if (imageStart >= 0 && (fileStart < 0 || imageStart < fileStart))
            {
                nextTagStart = imageStart;
                isImageTag = true;
            }
            else if (fileStart >= 0)
            {
                nextTagStart = fileStart;
                isImageTag = false;
            }
            
            // No tags found, just add the rest as text
            if (nextTagStart < 0)
            {
                if (currentPosition < content.Length)
                {
                    currentText.Append(content.Substring(currentPosition));
                }
                break;
            }
            
            // Add text before the tag
            if (nextTagStart > currentPosition)
            {
                currentText.Append(content.Substring(currentPosition, nextTagStart - currentPosition));
            }
            
            // Process the current tag
            int closeTagIndex = content.IndexOf(">", nextTagStart);
            if (closeTagIndex < 0)
            {
                // Malformed tag, treat as text
                currentText.Append(content.Substring(nextTagStart));
                break;
            }
            
            // Flush text before processing media
            if (currentText.Length > 0)
            {
                contentParts.Add(new { type = "text", text = currentText.ToString() });
                currentText.Clear();
            }
            
            string tagContent = content.Substring(nextTagStart, closeTagIndex - nextTagStart + 1);
            
            if (isImageTag)
            {
                var (mediaObj, newPosition) = ParseImageTag(content, nextTagStart);
                if (mediaObj != null)
                {
                    contentParts.Add(mediaObj);
                }
                
                currentPosition = newPosition;
            }
            else // file tag
            {
                var (fileObj, newPosition) = ParseFileTag(content, nextTagStart);
                if (fileObj != null)
                {
                    contentParts.Add(fileObj);
                }
                
                currentPosition = newPosition;
            }
        }
        
        // Add any remaining text
        if (currentText.Length > 0)
        {
            contentParts.Add(new { type = "text", text = currentText.ToString() });
        }
        
        return contentParts.ToArray();
    }
    
    // Parse image tag and extract base64 data and mime type
    private (object?, int) ParseImageTag(string content, int startIndex)
    {
        try
        {
            // Find the closing tag
            int endTag = content.IndexOf(">", startIndex);
            if (endTag < 0) return (null, startIndex + 6);

            // Parse the tag attributes
            string tag = content.Substring(startIndex, endTag - startIndex + 1);

            // Extract base64 data if present
            int base64Start = tag.IndexOf("base64=");
            if (base64Start > 0)
            {
                int dataStart = base64Start + 7; // length of "base64="

                // Find the quote character used
                char quoteChar = tag[dataStart];
                if (quoteChar != '"' && quoteChar != '\'') return (null, endTag + 1);

                // Find the closing quote
                int dataEnd = tag.IndexOf(quoteChar, dataStart + 1);
                if (dataEnd < 0) return (null, endTag + 1);

                string base64Data = tag.Substring(dataStart + 1, dataEnd - (dataStart + 1));

                // Get media type if specified, default to jpeg
                string mediaType = "image/jpeg";
                int typeStart = tag.IndexOf("type=");
                if (typeStart > 0)
                {
                    int typeValueStart = typeStart + 5; // length of "type="
                    char typeQuoteChar = tag[typeValueStart];
                    if (typeQuoteChar == '"' || typeQuoteChar == '\'')
                    {
                        int typeValueEnd = tag.IndexOf(typeQuoteChar, typeValueStart + 1);
                        if (typeValueEnd > 0)
                        {
                            mediaType = tag.Substring(typeValueStart + 1, typeValueEnd - (typeValueStart + 1));
                        }
                    }
                }

                // Create image object
                return (new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = mediaType,
                        data = base64Data
                    }
                }, endTag + 1);
            }

            // If no base64 data, treat as a text reference
            return (new { type = "text", text = "[Image]" }, endTag + 1);
        }
        catch
        {
            // If parsing fails, skip this tag
            return (null, startIndex + 6);
        }
    }

    private (object?, int) ParseFileTag(string content, int startIndex)
    {
        try
        {
            // Find the closing tag
            int endTag = content.IndexOf(">", startIndex);
            if (endTag < 0) return (null, startIndex + 5);

            // Parse the tag attributes
            string tag = content.Substring(startIndex, endTag - startIndex + 1);
            
            // Extract base64 data if present (for PDFs and other files that Claude can process)
            int base64Start = tag.IndexOf("base64=");
            if (base64Start > 0)
            {
                int dataStart = base64Start + 7; // length of "base64="
                char quoteChar = tag[dataStart];
                if (quoteChar != '"' && quoteChar != '\'') return (null, endTag + 1);

                int dataEnd = tag.IndexOf(quoteChar, dataStart + 1);
                if (dataEnd < 0) return (null, endTag + 1);

                string base64Data = tag.Substring(dataStart + 1, dataEnd - (dataStart + 1));

                // Get media type
                string mediaType = "application/pdf"; // Default to PDF
                int typeStart = tag.IndexOf("type=");
                if (typeStart > 0)
                {
                    int typeValueStart = typeStart + 5;
                    char typeQuoteChar = tag[typeValueStart];
                    if (typeQuoteChar == '"' || typeQuoteChar == '\'')
                    {
                        int typeValueEnd = tag.IndexOf(typeQuoteChar, typeValueStart + 1);
                        if (typeValueEnd > 0)
                        {
                            mediaType = tag.Substring(typeValueStart + 1, typeValueEnd - (typeValueStart + 1));
                        }
                    }
                }

                // Support PDFs and other file types Claude can handle
                if (mediaType == "application/pdf" || 
                    mediaType.StartsWith("text/") ||
                    mediaType == "application/json" || 
                    mediaType == "text/csv")
                {
                    return (new
                    {
                        type = "file",
                        source = new
                        {
                            type = "base64",
                            media_type = mediaType,
                            data = base64Data
                        }
                    }, endTag + 1);
                }
            }

            // For other file types or no base64, add a text reference
            int nameStart = tag.IndexOf("name=");
            string fileName = "File attachment";
            
            if (nameStart > 0)
            {
                int nameValueStart = nameStart + 5;
                char nameQuoteChar = tag[nameValueStart];
                if (nameQuoteChar == '"' || nameQuoteChar == '\'')
                {
                    int nameValueEnd = tag.IndexOf(nameQuoteChar, nameValueStart + 1);
                    if (nameValueEnd > 0)
                    {
                        fileName = tag.Substring(nameValueStart + 1, nameValueEnd - (nameValueStart + 1));
                    }
                }
            }
            
            return (new { type = "text", text = $"[File: {fileName}]" }, endTag + 1);
        }
        catch
        {
            // If parsing fails, skip this tag
            return (null, startIndex + 5);
        }
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

        int maxRetries = 3;
        int retryCount = 0;
        Dictionary<string, object>? currentRequestBody = requestBody as Dictionary<string, object>;

        while (!response.IsSuccessStatusCode && retryCount < maxRetries && currentRequestBody != null)
        {
            retryCount++;

            try
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                string errorType = "unknown";
                string errorParam = "none";

                try
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(errorContent);
                    if (errorJson.TryGetProperty("error", out var errorObj))
                    {
                        errorType = errorObj.TryGetProperty("type", out var type)
                            ? type.GetString() ?? "unknown"
                            : "unknown";
                        errorParam = errorObj.TryGetProperty("param", out var param)
                            ? param.GetString() ?? "none"
                            : "none";
                    }
                }
                catch
                {
                    // If we can't parse error details, continue with default values
                }

                var (correctionSuccess, retryResponse, correctedBody) =
                    await AttemptAutoCorrection(response, currentRequestBody, errorType, errorParam, "Anthropic");

                if (correctionSuccess && retryResponse != null && correctedBody != null)
                {
                    Console.WriteLine(
                        $"Auto-correction attempt {retryCount} successful, continuing with corrected request");
                    response = retryResponse;
                    currentRequestBody = correctedBody;
                }
                else
                {
                    Console.WriteLine($"Auto-correction attempt {retryCount} failed, giving up");
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during auto-correction attempt {retryCount}: {ex.Message}");
                break;
            }
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
    
    public static string ConvertFileToBase64(string filePath)
    {
        try
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            return Convert.ToBase64String(fileBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting file to base64: {ex.Message}");
            return string.Empty;
        }
    }

    public static string GetMimeTypeFromFilePath(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream" // Default binary file type
        };
    }
    
    // Method to prepare content for file attachment upload
    public static string PrepareFileAttachmentForMessage(string filePath, string fileType)
    {
        try
        {
            string base64Data = ConvertFileToBase64(filePath);
            if (string.IsNullOrEmpty(base64Data))
                return "[Unable to read file]";
                
            string mimeType = GetMimeTypeFromFilePath(filePath);
            
            if (mimeType.StartsWith("image/"))
            {
                // For images, create an image tag with base64 data
                return $"<image type=\"{mimeType}\" base64=\"{base64Data}\">";
            }
            else
            {
                // For other file types (PDFs, documents)
                return $"<file type=\"{mimeType}\" base64=\"{base64Data}\">";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error preparing file attachment: {ex.Message}");
            return $"[Error processing file: {ex.Message}]";
        }
    }
}