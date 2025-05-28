using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class AimlStreamChunkParser : BaseStreamChunkParser<AimlStreamChunkParser>
{
    public AimlStreamChunkParser(ILogger<AimlStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.AimlFlux;

    protected override ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        string? textDelta = null;
        string? finishReason = null;
        // AimlFlux does not typically involve token counts or complex tool calls in its stream format.

        Logger?.LogInformation("[AimlDebug] Raw chunk content: {RawContent}", rawJson);

        if (root.TryGetProperty("error", out var errorElement))
        {
            Logger?.LogError("AIML stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error");
        }

        // Check for raw content in the response (primary way AimlFlux sends text)
        if (root.TryGetProperty("rawContent", out var rawContentElement) && rawContentElement.ValueKind == JsonValueKind.String)
        {
            textDelta = rawContentElement.GetString();
            Logger?.LogInformation("Parsed AIML text content: '{TextDelta}'", textDelta);
        }
        // Alternative: some AIML might use a "delta" or "text" field directly
        else if (root.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.String)
        {
            textDelta = deltaElement.GetString();
            Logger?.LogInformation("Parsed AIML delta content: '{TextDelta}'", textDelta);
        }
        else if (root.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            textDelta = textElement.GetString();
            Logger?.LogInformation("Parsed AIML text content (from 'text' field): '{TextDelta}'", textDelta);
        }


        // Check if this is a completion chunk indicated by a specific field
        if (root.TryGetProperty("isCompletion", out var completionElement) && completionElement.ValueKind == JsonValueKind.True)
        {
            finishReason = "stop"; // Standardize to "stop"
            Logger?.LogInformation("AIML stream chunk indicates completion via isCompletion=true.");
        }

        // Check for any specific status or completion reason field
        if (root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String)
        {
            var status = statusElement.GetString()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(status))
            {
                switch (status)
                {
                    case "error":
                        finishReason = "error";
                        break;
                    case "completed":
                    case "done": // Common alternative for completion
                        finishReason = "stop";
                        break;
                    case "interrupted":
                        finishReason = "interrupted"; // Or map to a more standard one if needed
                        break;
                    default:
                        Logger?.LogWarning("Unknown AIML status: {Status}", status);
                        // Pass through if StreamProcessor is expected to handle it, or map to error/stop.
                        break;
                }
                Logger?.LogInformation("Parsed AIML status: {Status}, mapped to FinishReason: {FinishReason}", status, finishReason);
            }
        }
        
        // If text was received and no explicit finish reason, and a 'done' flag exists (common pattern)
        if (root.TryGetProperty("done", out var doneFlag) && doneFlag.ValueKind == JsonValueKind.True && string.IsNullOrEmpty(finishReason))
        {
            finishReason = "stop";
             Logger?.LogInformation("AIML stream chunk indicates completion via done=true.");
        }

        // AIML typically doesn't include token counts or tool calls in its stream chunks.
        // ToolCallInfo, InputTokens, OutputTokens will remain null.

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            FinishReason: finishReason
            // ThinkingDelta, ToolCallInfo, InputTokens, OutputTokens are omitted as they are not standard for AimlFlux streams
        );
    }
} 