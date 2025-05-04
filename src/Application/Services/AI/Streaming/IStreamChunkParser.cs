using Domain.Enums;

namespace Application.Services.AI.Streaming;

public interface IStreamChunkParser
{
    ParsedChunkInfo ParseChunk(string rawJson);
    ModelType SupportedModelType { get; }
}

public record ParsedChunkInfo(
    string? TextDelta = null,
    string? ThinkingDelta = null,
    ToolCallChunk? ToolCallInfo = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? FinishReason = null
);

public record ToolCallChunk(
    int Index,
    string? Id = null,
    string? Name = null,
    string? ArgumentChunk = null,
    bool IsComplete = false
); 