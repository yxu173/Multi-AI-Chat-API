using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Streaming;

public class AimlStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<AimlStreamChunkParser> _logger;

    public AimlStreamChunkParser(ILogger<AimlStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.AimlFlux;

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            string? finishReason = null;
            bool isCompletion = false;

            _logger?.LogInformation("[AimlDebug] Raw chunk content: {RawContent}", rawJson);

            if (root.TryGetProperty("error", out var errorElement))
            {
                _logger?.LogError("AIML stream reported error: {ErrorJson}", errorElement.GetRawText());
                return new ParsedChunkInfo(FinishReason: "error");
            }

            // Check for raw content in the response
            if (root.TryGetProperty("rawContent", out var rawContent) && rawContent.ValueKind == JsonValueKind.String)
            {
                textDelta = rawContent.GetString();
                _logger?.LogInformation("Parsed AIML text content: '{TextDelta}'", textDelta);
            }

            // Check if this is a completion chunk
            if (root.TryGetProperty("isCompletion", out var completionElement) && completionElement.ValueKind == JsonValueKind.True)
            {
                isCompletion = true;
                finishReason = "stop";
                _logger?.LogInformation("AIML stream chunk indicates completion");
            }

            // Check for any specific status or completion reason
            if (root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String)
            {
                var status = statusElement.GetString();
                if (!string.IsNullOrEmpty(status))
                {
                    switch (status.ToLowerInvariant())
                    {
                        case "error":
                            finishReason = "error";
                            break;
                        case "completed":
                            finishReason = "stop";
                            break;
                        case "interrupted":
                            finishReason = "interrupted";
                            break;
                    }
                    _logger?.LogInformation("Parsed AIML status: {Status}", status);
                }
            }

            // For AIML, we don't track token usage as it's not part of the standard format
            return new ParsedChunkInfo(
                TextDelta: textDelta,
                FinishReason: finishReason
            );
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse AIML stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing AIML stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
    }
} 