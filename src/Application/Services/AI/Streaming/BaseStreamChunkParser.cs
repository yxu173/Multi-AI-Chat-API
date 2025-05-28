using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public abstract class BaseStreamChunkParser<TParser> : IStreamChunkParser where TParser : IStreamChunkParser
{
    protected readonly ILogger<TParser> Logger;

    protected BaseStreamChunkParser(ILogger<TParser> logger)
    {
        Logger = logger;
    }

    public abstract ModelType SupportedModelType { get; }

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson))
        {
            Logger.LogWarning("Received empty or null raw JSON content.");
            return new ParsedChunkInfo(FinishReason: "error_empty_chunk");
        }

        try
        {
            // Log the raw content at a trace level for debugging if needed
            Logger.LogTrace("Attempting to parse raw JSON chunk: {RawJson}", rawJson);
            return ParseModelSpecificChunk(rawJson);
        }
        catch (JsonException jsonEx)
        {
            Logger.LogError(jsonEx, "Failed to parse stream chunk JSON. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error_json_parsing");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error parsing stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error_unexpected");
        }
    }

    protected abstract ParsedChunkInfo ParseModelSpecificChunk(string rawJson);
} 