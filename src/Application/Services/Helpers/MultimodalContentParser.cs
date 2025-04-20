using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Application.Services.Helpers;

public record ContentPart;
public record TextPart(string Text) : ContentPart;
public record ImagePart(string MimeType, string Base64Data, string? FileName = null) : ContentPart;
public record FilePart(string MimeType, string Base64Data, string FileName) : ContentPart;


public class MultimodalContentParser
{
    private readonly ILogger<MultimodalContentParser> _logger;
    private static readonly Regex MultimodalTagRegex =
        new Regex(@"<(image|file)-base64:(?:([^:]*?):)?([^;]*?);base64,([^>]*?)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public MultimodalContentParser(ILogger<MultimodalContentParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<ContentPart> Parse(string messageContent)
    {
        var contentParts = new List<ContentPart>();
        if (string.IsNullOrEmpty(messageContent)) return contentParts;
        var lastIndex = 0;

        try
        {
            foreach (Match match in MultimodalTagRegex.Matches(messageContent))
            {
                if (match.Index > lastIndex)
                {
                    string textBefore = messageContent.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrEmpty(textBefore)) contentParts.Add(new TextPart(textBefore));
                }

                string tagType = match.Groups[1].Value.ToLowerInvariant();
                string? potentialFileName = match.Groups[2].Value;
                string? potentialMimeType = match.Groups[3].Value;
                string base64Data = match.Groups[4].Value;

                // Basic validation
                if (string.IsNullOrWhiteSpace(base64Data))
                {
                    _logger?.LogWarning("Malformed tag (missing base64 data): {Tag}", match.Value);
                    contentParts.Add(new TextPart(match.Value)); 
                    lastIndex = match.Index + match.Length;
                    continue;
                }

                // Extract and validate MimeType
                string? mimeType = potentialMimeType?.Trim();
                if (string.IsNullOrEmpty(mimeType) || !mimeType.Contains('/'))
                {
                    _logger?.LogWarning("Malformed tag (invalid or missing mime type '{MimeType}'): {Tag}", mimeType, match.Value);
                    contentParts.Add(new TextPart(match.Value));
                    lastIndex = match.Index + match.Length;
                    continue;
                }

                if (tagType == "image")
                {
                    contentParts.Add(new ImagePart(mimeType, base64Data, potentialFileName));
                }
                else if (tagType == "file")
                {
                    string? fileName = potentialFileName?.Trim();
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        contentParts.Add(new FilePart(mimeType, base64Data, fileName));
                    }
                    else
                    {
                        _logger?.LogWarning("Malformed file tag (missing filename): {Tag}", match.Value);
                        contentParts.Add(new TextPart(match.Value));
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < messageContent.Length)
            {
                string textAfter = messageContent.Substring(lastIndex);
                if (!string.IsNullOrEmpty(textAfter)) contentParts.Add(new TextPart(textAfter));
            }

            // If parsing only resulted in whitespace text parts, but original was not whitespace, return original
             if (!contentParts.Any(p => !(p is TextPart tp && string.IsNullOrWhiteSpace(tp.Text))) && !string.IsNullOrWhiteSpace(messageContent))
            {
                _logger?.LogWarning("Multimodal parsing resulted in no valid parts, returning original content as a single TextPart.");
                return new List<ContentPart> { new TextPart(messageContent) };
            }

            // Combine consecutive text parts
            var combinedParts = new List<ContentPart>();
            StringBuilder? currentText = null;
            foreach (var part in contentParts)
            {
                if (part is TextPart tp)
                {
                    if (currentText == null) currentText = new StringBuilder();
                    currentText.Append(tp.Text); // Append raw text
                }
                else
                {
                    if (currentText != null && currentText.Length > 0)
                    {
                        // Trim only when adding the combined text part
                        combinedParts.Add(new TextPart(currentText.ToString().Trim()));
                        currentText = null;
                    }
                    combinedParts.Add(part); // Add the non-text part
                }
            }
            // Add any remaining text
            if (currentText != null && currentText.Length > 0)
            {
                 combinedParts.Add(new TextPart(currentText.ToString().Trim()));
            }

            // Final filter to remove empty text parts that might result from trimming
            return combinedParts.Where(p => !(p is TextPart tp && string.IsNullOrEmpty(tp.Text))).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during multimodal content parsing for content length: {ContentLength}. Returning original content.", messageContent.Length);
            // Return original content as a single TextPart on error
            return new List<ContentPart> { new TextPart(messageContent) };
        }
    }
} 