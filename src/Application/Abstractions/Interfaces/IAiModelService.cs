using System.Collections.Generic;
using System.Threading;
using Application.Services;

namespace Application.Abstractions.Interfaces;

/// <summary>
/// Represents the pre-formatted request payload to be sent to a specific AI provider API.
/// The Application layer is responsible for creating this object.
/// </summary>
/// <param name="Payload">The object (e.g., Dictionary<string, object> or specific DTO) to be serialized as the request body.</param>
public record AiRequestPayload(object Payload);

/// <summary>
/// Represents a raw chunk of data received from the AI provider's streaming API.
/// The Application layer is responsible for parsing this into meaningful content.
/// </summary>
/// <param name="RawContent">The raw data chunk (e.g., a JSON string).</param>
/// <param name="IsCompletion">Indicates if this chunk signifies the end of the stream (e.g., contains final token counts or a stop reason).</param>
public record AiRawStreamChunk(string RawContent, bool IsCompletion = false);

/// <summary>
/// Interface for interacting with AI models
/// </summary>
public interface IAiModelService
{
    /// <summary>
    /// Streams a response from the AI model based on a pre-formatted request payload.
    /// </summary>
    /// <param name="request">The pre-formatted request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous stream of raw data chunks from the AI provider.</returns>
    IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload request,
        CancellationToken cancellationToken);
}

public record StreamResponse(string Content, int InputTokens, int OutputTokens, bool IsThinking = false);

// ============================================================
// File Uploading Abstraction
// ============================================================

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

/// <summary>
/// Defines the contract for a service that can upload files compatible with an AI provider.
/// This might be implemented by the same service that handles chat completions (like GeminiService)
/// or a separate dedicated service.
/// </summary>
public interface IAiFileUploader
{
    /// <summary>
    /// Uploads file data to the provider's file storage.
    /// </summary>
    /// <param name="fileBytes">The raw byte content of the file.</param>
    /// <param name="mimeType">The MIME type of the file.</param>
    /// <param name="fileName">The original name of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing details of the uploaded file, or null if upload fails.</returns>
    Task<AiFileUploadResult?> UploadFileForAiAsync(
        byte[] fileBytes,
        string mimeType,
        string fileName,
        CancellationToken cancellationToken);
}