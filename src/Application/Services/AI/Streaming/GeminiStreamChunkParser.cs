using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class GeminiStreamChunkParser : BaseStreamChunkParser<GeminiStreamChunkParser>
{
    public GeminiStreamChunkParser(ILogger<GeminiStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.Gemini;

    protected override ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        string? textDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        bool containsFunctionCall = false; // Tracks if a function call is part of the current chunk

        Logger?.LogInformation("[GeminiDebug] Raw chunk content: {RawContent}", rawJson);

        // Check for top-level error property
        if (root.TryGetProperty("error", out var errorElement))
        {
            Logger?.LogError("Gemini stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error"); // Propagate error as finish reason
        }

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            Logger?.LogTrace("[GeminiDebug] Candidate content: {Candidate}", firstCandidate.GetRawText());

            if (firstCandidate.TryGetProperty("finishReason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
            {
                var reason = reasonElement.GetString()?.ToUpperInvariant();
                Logger?.LogDebug("Found Gemini finishReason: {Reason}", reason);

                switch (reason)
                {
                    case "STOP":
                        finishReason = "stop";
                        break;
                    case "MAX_TOKENS":
                        finishReason = "length";
                        break;
                    case "SAFETY":
                    case "RECITATION": // Assuming recitation issues are a form of content filter/safety
                        finishReason = "content_filter";
                        break;
                    case "TOOL_CALLS": // This is the specific reason Gemini uses
                    case "FUNCTION_CALL": // Older or alternative naming
                        finishReason = "tool_calls";
                        containsFunctionCall = true; // Explicitly note tool call presence from finish reason
                        break;
                    case null: // No explicit reason yet
                        break;
                    default:
                        Logger?.LogWarning("Unknown Gemini finish reason: {Reason}", reason);
                        // Potentially map to a generic error or pass through if StreamProcessor can handle unknown reasons
                        finishReason = reason; // Pass through for now
                        break;
                }
            }

            if (firstCandidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = (textDelta ?? string.Empty) + textElement.GetString(); // Append if multiple text parts
                        Logger?.LogTrace("Parsed Gemini text content: '{TextDelta}'", textDelta);
                    }
                    else if (part.TryGetProperty("functionCall", out var functionCall))
                    {
                        containsFunctionCall = true; // Mark that a function call structure was found
                        string? funcName = null;
                        string? funcArgs = null;
                        // Gemini function calls are typically not chunked for arguments in the same way as OpenAI.
                        // They usually appear fully formed in one part if finishReason is TOOL_CALLS.
                        if (functionCall.TryGetProperty("name", out var nameElement))
                        {
                            funcName = nameElement.GetString();
                        }
                        if (functionCall.TryGetProperty("args", out var argsElement))
                        {
                            // Gemini args are an object, not a string. StreamProcessor expects string.
                            funcArgs = argsElement.GetRawText(); 
                        }

                        if (funcName != null)
                        {
                            // Gemini doesn't provide a tool_call_id in the stream in the same way OpenAI does.
                            // StreamProcessor generates one if needed, or we can generate a temporary one.
                            // For simplicity, StreamProcessor will handle ID generation if this toolCallInfo is used.
                            // Index is also not explicitly per-call in Gemini as it is in Anthropic/OpenAI tool deltas.
                            // We'll use a default index of 0 for now, as Gemini usually sends one tool call at a time in this structure.
                            toolCallInfo = new ToolCallChunk(0, Name: funcName, ArgumentChunk: funcArgs, IsComplete: true);
                            Logger?.LogDebug("Parsed Gemini function call: {Name}", funcName);
                        }
                    }
                }
            }
        }

        // Usage metadata might appear in chunks without candidate data (e.g., final chunk)
        if (root.TryGetProperty("usageMetadata", out var usageMetadata))
        {
            Logger?.LogTrace("Found Gemini usageMetadata: {UsageMetadataRaw}", usageMetadata.GetRawText());
            if (usageMetadata.TryGetProperty("promptTokenCount", out var pToken) && pToken.ValueKind == JsonValueKind.Number)
            {
                inputTokens = pToken.GetInt32();
            }
            // Gemini sometimes sends totalTokenCount, sometimes candidatesTokenCount
            if (usageMetadata.TryGetProperty("candidatesTokenCount", out var cToken) && cToken.ValueKind == JsonValueKind.Number)
            {
                outputTokens = cToken.GetInt32();
            }
            else if (usageMetadata.TryGetProperty("totalTokenCount", out var tToken) && tToken.ValueKind == JsonValueKind.Number)
            {
                // If we have input tokens, we can derive output, otherwise totalTokenCount might be just output or combined.
                // This heuristic might need refinement based on observing more Gemini stream patterns.
                if (inputTokens.HasValue)
                {
                    outputTokens = tToken.GetInt32() - inputTokens.Value;
                }
                else
                {
                    // If no promptTokenCount seen yet, assume totalTokenCount is effectively output for this chunk or final sum.
                    outputTokens = tToken.GetInt32(); 
                }
            }

            if (inputTokens.HasValue || outputTokens.HasValue)
            {
                Logger?.LogDebug("Parsed Gemini token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
            }
        }
        
        // If a functionCall part was found, and no explicit tool_calls finish_reason, set it.
        // This ensures that StreamProcessor correctly identifies the intent for tool execution.
        if (containsFunctionCall && finishReason != "tool_calls")
        {
            finishReason = "tool_calls";
            Logger?.LogDebug("Overriding finish reason to 'tool_calls' due to detected functionCall part.");
        }

        Logger?.LogTrace("[GeminiSummary] Processed chunk: TextDelta='{TextDelta}', ToolCallName={ToolName}, FinishReason={FR}",
            textDelta,
            toolCallInfo?.Name,
            finishReason);

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }
} 