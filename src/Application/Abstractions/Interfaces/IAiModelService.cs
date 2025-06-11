using System.Collections.Generic;
using System.Threading;
using Application.Services;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Application.Services.Messaging;

namespace Application.Abstractions.Interfaces;



public record ToolResultFormattingContext(string ToolCallId, string ToolName, string Result, bool WasSuccessful);

public interface IAiModelService
{
    IAsyncEnumerable<ParsedChunkInfo> StreamResponseAsync(
        AiRequestPayload request,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null);

    Task<MessageDto> FormatToolResultAsync(ToolResultFormattingContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the result of uploading a file to an AI provider's file service.
/// </summary>
public record AiFileUploadResult(
    string ProviderFileId, // The ID assigned by the provider (e.g., "files/abc123xyz")
    string Uri,            // The URI to reference the file in API calls (might be the same as ID or different)
    string MimeType,
    long SizeBytes,
    string? OriginalFileName = null // Optional: Store original filename for reference
);

public interface IAiFileUploader
{

    Task<AiFileUploadResult?> UploadFileForAiAsync(
        byte[] fileBytes,
        string mimeType,
        string fileName,
        CancellationToken cancellationToken);
}