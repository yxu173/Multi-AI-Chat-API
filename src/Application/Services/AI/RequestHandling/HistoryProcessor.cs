using System.Text.RegularExpressions;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.Messaging;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.RequestHandling;

public class HistoryProcessor : IHistoryProcessor
{
    private static readonly Regex _imageTagRegex = new(@"<image:([0-9a-fA-F-]{36})>", RegexOptions.Compiled);
    private static readonly Regex _fileTagRegex = new(@"<file:([0-9a-fA-F-]{36}):([^>]*)>", RegexOptions.Compiled);
    
    private readonly ILogger<HistoryProcessor> _logger;
    private readonly IFileAttachmentService _fileService;

    public HistoryProcessor(
        IFileAttachmentService fileService,
        ILogger<HistoryProcessor> logger)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<MessageDto>> ProcessAsync(
        IEnumerable<MessageDto> history,
        CancellationToken cancellationToken = default)
    {
        var processedHistory = new List<MessageDto>();
        
        foreach (var message in history)
        {
            var processedContent = await ProcessFileTagsAsync(message.Content, cancellationToken);
            
            var processedMessage = new MessageDto(
                processedContent, 
                message.IsFromAi, 
                message.MessageId)
            {
                FileAttachments = message.FileAttachments,
                ThinkingContent = message.ThinkingContent,
                FunctionCall = message.FunctionCall,
                FunctionResponse = message.FunctionResponse
            };
            
            processedHistory.Add(processedMessage);
        }
        
        return processedHistory;
    }

    private async Task<string> ProcessFileTagsAsync(string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        try
        {
            var imageMatches = _imageTagRegex.Matches(content);
            foreach (Match match in imageMatches)
            {
                if (Guid.TryParse(match.Groups[1].Value, out Guid fileId))
                {
                    var base64Data = await _fileService.GetBase64Async(fileId, cancellationToken);
                    if (base64Data != null && !string.IsNullOrEmpty(base64Data.Base64Content))
                    {
                        var replacement = $"<image-base64:{base64Data.ContentType};base64,{base64Data.Base64Content}>";
                        content = content.Replace(match.Value, replacement);
                    }
                    else
                    {
                        content = content.Replace(match.Value, "[Image could not be processed]");
                    }
                }
            }

            var fileMatches = _fileTagRegex.Matches(content);
            foreach (Match match in fileMatches)
            {
                if (Guid.TryParse(match.Groups[1].Value, out Guid fileId))
                {
                    string fileName = match.Groups[2].Value;
                    var base64Data = await _fileService.GetBase64Async(fileId, cancellationToken);
                    if (base64Data != null && !string.IsNullOrEmpty(base64Data.Base64Content))
                    {
                        var replacement = $"<file-base64:{fileName}:{base64Data.ContentType};base64,{base64Data.Base64Content}>";
                        content = content.Replace(match.Value, replacement);
                    }
                    else
                    {
                        content = content.Replace(match.Value, $"[File {fileName} could not be processed]");
                    }
                }
            }

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file tags in content");
            return content;
        }
    }
}
