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
            // Use Utf8JsonReader for more efficient parsing
            return ParseChunkWithUtf8Reader(rawJson);
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

    private ParsedChunkInfo ParseChunkWithUtf8Reader(string rawJson)
    {
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(rawJson);
        var reader = new Utf8JsonReader(jsonBytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        return ParseModelSpecificChunkWithReader(ref reader);
    }

    protected virtual ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        try
        {
            // Fallback to JsonDocument for complex parsing scenarios
            using var jsonDoc = JsonDocument.Parse(rawJson);
            return ParseModelSpecificChunkInternal(jsonDoc);
        }
        catch (JsonException jsonEx)
        {
            Logger.LogError(jsonEx, "Failed to parse stream chunk JSON. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error_json_parsing");
        }
    }

    protected abstract ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc);
    
    // New method for efficient parsing with Utf8JsonReader
    protected abstract ParsedChunkInfo ParseModelSpecificChunkWithReader(ref Utf8JsonReader reader);
}