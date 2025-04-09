using System.Collections.Generic;
using System.Threading;
using Application.Services;

namespace Application.Abstractions.Interfaces;


//public record AiRequestPayload(object Payload);

public record AiRawStreamChunk(string RawContent, bool IsCompletion = false);

public interface IAiModelService
{
    IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload request,
        CancellationToken cancellationToken);
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