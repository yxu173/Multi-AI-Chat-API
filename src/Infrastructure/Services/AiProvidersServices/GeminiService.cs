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
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

public class GeminiService : BaseAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public GeminiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode,
        UserAiModelSettings? modelSettings = null, AiModel? aiModel = null,
        ModelParameters? customModelParameters = null)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl, modelSettings, aiModel, customModelParameters)
    {
    }

    protected override void ConfigureHttpClient()
    {
    }

    protected override string GetEndpointPath() => $"v1beta/models/{ModelCode}:streamGenerateContent?key={ApiKey}";

    protected override List<(string Role, string Content)> PrepareMessageList(IEnumerable<MessageDto> history)
    {
        var messages = base.PrepareMessageList(history);
        return messages;
    }

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = PrepareMessageList(history);
        var contents = new List<object>();

        foreach (var message in messages)
        {
            var role = message.Role == "assistant" ? "model" : "user";
            var parts = new List<object>();
            string content = message.Content;

            if (role == "user")
            {
                var regex = new Regex(@"<(image|file)-base64:([^;>]+);base64,([^>]+)>");
                var matches = regex.Matches(content);
                int lastIndex = 0;

                foreach (Match match in matches)
                {
                    if (match.Index > lastIndex)
                    {
                        string textBefore = content.Substring(lastIndex, match.Index - lastIndex).Trim();
                        if (!string.IsNullOrEmpty(textBefore))
                        {
                            parts.Add(new { text = textBefore });
                        }
                    }

                    string tagType = match.Groups[1].Value;
                    string metaData = match.Groups[2].Value;
                    string base64Data = match.Groups[3].Value;

                    if (tagType == "image")
                    {
                        string mediaType = metaData;
                        if (IsValidGeminiImageType(mediaType, out string normalizedMediaType))
                        {
                            parts.Add(new
                            {
                                inlineData = new
                                {
                                    mimeType = normalizedMediaType,
                                    data = base64Data
                                }
                            });
                        }
                        else
                        {
                            parts.Add(new { text = $"[Image: Unsupported format '{mediaType}']" });
                        }
                    }
                    else if (tagType == "file")
                    {
                        string fileName = "unknown";
                        string fileContentType = "unknown";
                        var metaParts = metaData.Split(new[] { ':' }, 2);
                        if (metaParts.Length > 0) fileName = metaParts[0];
                        if (metaParts.Length > 1) fileContentType = metaParts[1];

                        try
                        {
                            byte[] fileBytes = Convert.FromBase64String(base64Data);

                            if (fileContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                string extractedText = ExtractTextFromPdf(fileBytes, fileName);
                                parts.Add(new { text = extractedText });
                            }
                            else if (IsPlainTextContentType(fileContentType))
                            {
                                string extractedText = Encoding.UTF8.GetString(fileBytes);
                                parts.Add(new
                                {
                                    text = $"[Content from File: {fileName} ({fileContentType})]\n\n{extractedText}"
                                });
                            }
                            else
                            {
                                parts.Add(new
                                {
                                    text = $"[Uploaded File: {fileName} ({fileContentType}) - Content not embedded]"
                                });
                            }
                        }
                        catch (FormatException ex)
                        {
                            Console.WriteLine($"Error decoding base64 data for file {fileName}: {ex.Message}");
                            parts.Add(new { text = $"[Error processing file: {fileName} - Invalid base64 data]" });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing file {fileName} ({fileContentType}): {ex.Message}");
                            parts.Add(new { text = $"[Error processing file: {fileName} ({fileContentType})]" });
                        }
                    }

                    lastIndex = match.Index + match.Length;
                }

                if (lastIndex < content.Length)
                {
                    string textAfter = content.Substring(lastIndex).Trim();
                    if (!string.IsNullOrEmpty(textAfter))
                    {
                        parts.Add(new { text = textAfter });
                    }
                }

                if (matches.Count == 0)
                {
                    parts.Add(new { text = content });
                }
                else if (parts.Count == 0 && !string.IsNullOrWhiteSpace(content))
                {
                    parts.Add(new
                        { text = "[Message included attachments, but none could be processed or added as parts]" });
                }
            }
            else
            {
                parts.Add(new { text = content });
            }

            if (role == "user" && parts.Count == 0 && !string.IsNullOrWhiteSpace(content))
            {
                parts.Add(new { text = "[User message contained only unsupported attachments]" });
            }
            else if (role == "model" && parts.Count == 0 && !string.IsNullOrWhiteSpace(content))
            {
                parts.Add(new { text = content });
            }

            if (parts.Any())
            {
                contents.Add(new { role, parts });
            }
            else if (!string.IsNullOrWhiteSpace(content))
            {
                contents.Add(new { role, parts = new List<object> { new { text = content } } });
            }
        }

        contents = EnsureAlternatingRoles(contents);

        var parameters = GetModelParameters();
        var generationConfig = new Dictionary<string, object>();
        var safetySettings = new List<object>
        {
            new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
        };

        var supportedParams = new HashSet<string>() { "temperature", "topP", "topK", "maxOutputTokens" };

        if (parameters.ContainsKey("temperature")) generationConfig["temperature"] = parameters["temperature"];
        if (parameters.ContainsKey("top_p")) generationConfig["topP"] = parameters["top_p"];
        if (parameters.ContainsKey("top_k")) generationConfig["topK"] = parameters["top_k"];
        if (parameters.ContainsKey("max_tokens")) generationConfig["maxOutputTokens"] = parameters["max_tokens"];
        if (parameters.ContainsKey("stop")) generationConfig["stopSequences"] = parameters["stop"];

        var keysToRemove = generationConfig.Keys.Where(k => !supportedParams.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            Console.WriteLine($"Removing unsupported parameter for Gemini: {key}");
            generationConfig.Remove(key);
        }

        var requestObj = new { contents, generationConfig, safetySettings };
        return requestObj;
    }

    private string ExtractTextFromPdf(byte[] pdfBytes, string fileName)
    {
        try
        {
            using (var document = PdfDocument.Open(pdfBytes))
            {
                var textContent = new StringBuilder();
                textContent.AppendLine($"[Content from PDF: {fileName}]");
                textContent.AppendLine("--- START OF PDF CONTENT ---");
                foreach (Page page in document.GetPages())
                {
                    textContent.AppendLine(page.Text);
                }

                textContent.AppendLine("--- END OF PDF CONTENT ---");
                return textContent.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting text from PDF {fileName}: {ex.Message}");
            return $"[Error extracting content from PDF: {fileName}. Reason: {ex.Message}]";
        }
    }

    private bool IsPlainTextContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        var typeLower = contentType.ToLowerInvariant().Trim();

        return typeLower.StartsWith("text/") ||
               typeLower == "application/json" ||
               typeLower == "application/xml" ||
               typeLower == "application/javascript";
    }

    private bool IsValidGeminiImageType(string mediaType, out string normalizedMediaType)
    {
        string typeLower = mediaType.ToLowerInvariant().Trim();
        var supportedTypes = new Dictionary<string, string>
        {
            { "image/png", "image/png" },
            { "image/jpeg", "image/jpeg" },
            { "image/jpg", "image/jpeg" },
            { "image/heic", "image/heic" },
            { "image/heif", "image/heif" },
            { "image/webp", "image/webp" }
        };

        if (supportedTypes.TryGetValue(typeLower, out var geminiType))
        {
            normalizedMediaType = geminiType;
            return true;
        }

        normalizedMediaType = string.Empty;
        return false;
    }

    private List<object> EnsureAlternatingRoles(List<object> originalContents)
    {
        if (!originalContents.Any()) return originalContents;

        var mergedContents = new List<object>();
        var currentRole = "";
        var currentParts = new List<object>();

        foreach (var contentItem in originalContents)
        {
            var itemRole = (string)((dynamic)contentItem).role;
            var itemParts = (List<object>)((dynamic)contentItem).parts;

            if (itemRole == currentRole)
            {
                currentParts.AddRange(itemParts);
            }
            else
            {
                if (!string.IsNullOrEmpty(currentRole) && currentParts.Any())
                {
                    mergedContents.Add(new { role = currentRole, parts = currentParts });
                }

                currentRole = itemRole;
                currentParts = new List<object>(itemParts);
            }
        }

        if (!string.IsNullOrEmpty(currentRole) && currentParts.Any())
        {
            mergedContents.Add(new { role = currentRole, parts = currentParts });
        }

        if (mergedContents.Any() && ((dynamic)mergedContents.First()).role == "model")
        {
            mergedContents.Insert(0, new { role = "user", parts = new List<object> { new { text = "" } } });
        }

        return mergedContents;
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        HttpResponseMessage? response = null;
        bool requestSucceeded = false;

        try
        {
            var initialRequest = CreateRequest(requestBody);
            response = await HttpClient.SendAsync(initialRequest, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                int maxRetries = 3;
                int retryCount = 0;
                while (!response.IsSuccessStatusCode && retryCount < maxRetries)
                {
                    retryCount++;
                    string errorContent = "";
                    try
                    {
                        errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    catch
                    {
                        /* Ignore read error */
                    }

                    Console.WriteLine(
                        $"Gemini API Error (attempt {retryCount}): {response.StatusCode}. Content: {errorContent}");

                    response.Dispose(); // Dispose failed response before retry
                    await Task.Delay(1000 * retryCount, cancellationToken);

                    Console.WriteLine($"Retrying Gemini request (attempt {retryCount})");
                    var retryRequest = CreateRequest(requestBody);
                    response = await HttpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                }
            }

            if (response.IsSuccessStatusCode)
            {
                requestSucceeded = true;
            }
            else
            {
                Console.WriteLine(
                    $"Gemini request failed definitively after retries with status: {response.StatusCode}");
                response.Dispose();
                response = null; 
                yield break; 
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"Critical error during Gemini request setup or retry: {ex.Message}");
            response?.Dispose(); 
            throw; 
        }

        if (requestSucceeded && response != null)
        {
            try
            {
                await foreach (var streamResponse in ProcessGeminiStream(response, cancellationToken)
                                   .WithCancellation(cancellationToken))
                {
                    yield return streamResponse;
                }
            }
            finally
            {
                response.Dispose();
            }
        }
        
    }

    private async IAsyncEnumerable<StreamResponse> ProcessGeminiStream(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var fullResponse = new StringBuilder();
        int promptTokens = 0;
        int currentOutputTokens = 0;
        int finalOutputTokens = -1;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var jsonElement in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            string? currentTextChunk = null;
            if (jsonElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                currentTextChunk = textElement.GetString();
            }

            if (jsonElement.TryGetProperty("usageMetadata", out var usageMetadata))
            {
                if (usageMetadata.TryGetProperty("promptTokenCount", out var promptTokenElement))
                {
                    promptTokens = promptTokenElement.GetInt32();
                }

                if (usageMetadata.TryGetProperty("candidatesTokenCount", out var candidatesTokenElement))
                {
                    finalOutputTokens = candidatesTokenElement.GetInt32();
                }
                else if (finalOutputTokens == -1 &&
                         usageMetadata.TryGetProperty("totalTokenCount", out var totalTokenElement))
                {
                    if (promptTokens > 0)
                    {
                        finalOutputTokens = totalTokenElement.GetInt32() - promptTokens;
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentTextChunk))
            {
                fullResponse.Append(currentTextChunk);
                currentOutputTokens =
                    finalOutputTokens != -1 ? finalOutputTokens : Math.Max(1, fullResponse.Length / 4);
                yield return new StreamResponse(currentTextChunk, promptTokens, currentOutputTokens);
            }
            else if (finalOutputTokens != -1 && currentOutputTokens != finalOutputTokens)
            {
                currentOutputTokens = finalOutputTokens;
                yield return new StreamResponse("", promptTokens, currentOutputTokens);
            }
        }
    }
}