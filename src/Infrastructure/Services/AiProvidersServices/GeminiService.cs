using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var baseRequestObj = (Dictionary<string, object>)base.CreateRequestBody(history);

        var baseMessages = base.PrepareMessageList(history);

        var geminiContents = new List<object>();
        foreach (var (role, content) in baseMessages)
        {
            string geminiRole = role switch {
                "assistant" => "model",
                "user" => "user",
                _ => null
            };

            if (geminiRole == null) continue;

            var parts = new List<object>();
            if (geminiRole == "user")
            {
                var contentParts = ParseMultimodalContent(content);

                foreach(var part in contentParts)
                {
                    switch (part)
                    {
                        case TextPart textPart:
                            parts.Add(new { text = textPart.Text });
                            break;
                        case ImagePart imagePart:
                            if (IsValidGeminiImageType(imagePart.MimeType, out string normalizedMediaType))
                            {
                                parts.Add(new {
                                    inlineData = new {
                                        mimeType = normalizedMediaType,
                                        data = imagePart.Base64Data
                                    }
                                });
                            }
                            else
                            {
                                parts.Add(new { text = $"[Image: Unsupported format '{imagePart.MimeType}']" });
                            }
                            break;
                        case FilePart filePart:
                            parts.AddRange(ProcessGeminiFilePart(filePart));
                            break;
                    }
                }

                if (parts.Count == 0 && !string.IsNullOrWhiteSpace(content))
                {
                    parts.Add(new { text = "[User message contained only unsupported attachments or processing failed]" });
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    parts.Add(new { text = content });
                }
            }

            if (parts.Any())
            {
                 geminiContents.Add(new { role = geminiRole, parts });
            }
            else if (!string.IsNullOrWhiteSpace(content))
            {
                 geminiContents.Add(new { role = geminiRole, parts = new List<object> { new { text = content } } });
            }
        }

        geminiContents = EnsureAlternatingRoles(geminiContents);

        var generationConfig = new Dictionary<string, object>();
        var safetySettings = GetGeminiSafetySettings();

        var supportedGeminiParams = new HashSet<string>() { "temperature", "topP", "topK", "maxOutputTokens", "stopSequences" };

        foreach(var kvp in baseRequestObj)
        {
            if (supportedGeminiParams.Contains(kvp.Key))
            {
                generationConfig[kvp.Key] = kvp.Value;
            }
            else if (kvp.Key != "model" && kvp.Key != "stream")
            {
                 Console.WriteLine($"Gemini Specific: Ignoring parameter '{kvp.Key}' not used in generationConfig.");
            }
        }

        var finalRequestBody = new
        {
            contents = geminiContents,
            generationConfig,
            safetySettings
        };

        return finalRequestBody;
    }

    private List<object> ProcessGeminiFilePart(FilePart filePart)
    {
        var parts = new List<object>();
        try
        {
            byte[] fileBytes = Convert.FromBase64String(filePart.Base64Data);

            if (filePart.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                string extractedText = ExtractTextFromPdf(fileBytes, filePart.FileName);
                parts.Add(new { text = extractedText });
            }
            else if (IsPlainTextContentType(filePart.MimeType))
            {
                string extractedText = Encoding.UTF8.GetString(fileBytes);
                parts.Add(new { text = $"[Content from File: {filePart.FileName} ({filePart.MimeType})]\n\n{extractedText}" });
            }
            else
            {
                parts.Add(new { text = $"[Uploaded File: {filePart.FileName} ({filePart.MimeType}) - Content not embedded]" });
            }
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"Error decoding base64 data for file {filePart.FileName}: {ex.Message}");
            parts.Add(new { text = $"[Error processing file: {filePart.FileName} - Invalid base64 data]" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing file {filePart.FileName} ({filePart.MimeType}): {ex.Message}");
            parts.Add(new { text = $"[Error processing file: {filePart.FileName} ({filePart.MimeType})]" });
        }
        return parts;
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
               typeLower.EndsWith("/json") ||
               typeLower.EndsWith("/xml") ||
               typeLower.EndsWith("/javascript") ||
               typeLower == "application/rtf";
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
            dynamic item = contentItem;
            string itemRole = item.role;
            List<object> itemParts = item.parts;

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

        if (mergedContents.Any())
        {
            dynamic firstItem = mergedContents.First();
            if (firstItem.role == "model")
            {
                mergedContents.Insert(0, new { role = "user", parts = new List<object> { new { text = "" } } });
            }
        }

        return mergedContents;
    }

    private List<object> GetGeminiSafetySettings()
    {
        return new List<object>
        {
            new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
        };
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = CreateRequestBody(history);
        HttpResponseMessage? response = null;

        int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = CreateRequest(requestBody);
                response?.Dispose();
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                string errorContent = "";
                try { errorContent = await response.Content.ReadAsStringAsync(cancellationToken); } catch { /* Ignore read error */ }
                Console.WriteLine($"Gemini API Error (Attempt {attempt}/{maxRetries}): {response.StatusCode}. Content: {errorContent}");

                if (attempt == maxRetries)
                {
                    await HandleApiErrorAsync(response, "Gemini");
                    yield break;
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 Console.WriteLine("Gemini stream request cancelled.");
                 response?.Dispose();
                 yield break;
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Critical error during Gemini request (Attempt {attempt}/{maxRetries}): {ex.Message}");
                 if (attempt == maxRetries)
                 {
                     response?.Dispose();
                     throw;
                 }
                 await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            Console.WriteLine("Gemini request failed after all attempts or due to an unhandled exception pathway.");
            response?.Dispose();
            yield break;
        }

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
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true },
                           cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            string? currentTextChunk = null;

            if (jsonElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                currentTextChunk = textElement.GetString();
            }

            if (jsonElement.TryGetProperty("usageMetadata", out var usageMetadata))
            {
                if (usageMetadata.TryGetProperty("promptTokenCount", out var promptTokenElement) && promptTokenElement.TryGetInt32(out var pt))
                {
                    promptTokens = pt;
                }

                if (usageMetadata.TryGetProperty("candidatesTokenCount", out var candidatesTokenElement) && candidatesTokenElement.TryGetInt32(out var ct))
                {
                    finalOutputTokens = ct;
                }
                else if (finalOutputTokens == -1 && usageMetadata.TryGetProperty("totalTokenCount", out var totalTokenElement) && totalTokenElement.TryGetInt32(out var tt))
                {
                    if (promptTokens > 0)
                    {
                        // This might represent the running total, treat with caution
                        // finalOutputTokens = tt - promptTokens;
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentTextChunk))
            {
                fullResponse.Append(currentTextChunk);
                currentOutputTokens = (finalOutputTokens != -1) ? finalOutputTokens : Math.Max(1, fullResponse.Length / 4);
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