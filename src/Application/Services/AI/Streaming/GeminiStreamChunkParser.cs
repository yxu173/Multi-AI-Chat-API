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

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        bool containsFunctionCall = false; 

        Logger?.LogInformation("[GeminiDebug] Raw chunk content: {RawContent}", jsonDoc.RootElement.GetRawText());

        if (root.TryGetProperty("error", out var errorElement))
        {
            Logger?.LogError("Gemini stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error");
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
                    case "RECITATION": 
                        finishReason = "content_filter";
                        break;
                    case "TOOL_CALLS": 
                    case "FUNCTION_CALL": 
                        finishReason = "tool_calls";
                        containsFunctionCall = true; 
                        break;
                    case null: 
                        break;
                    default:
                        Logger?.LogWarning("Unknown Gemini finish reason: {Reason}", reason);
                        finishReason = reason; 
                        break;
                }
            }

            if (firstCandidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        var text = textElement.GetString();
                        
                        // Check if this is a thought part
                        if (part.TryGetProperty("thought", out var thoughtElement) && thoughtElement.ValueKind == JsonValueKind.True)
                        {
                            thinkingDelta = (thinkingDelta ?? string.Empty) + text;
                            Logger?.LogTrace("Parsed Gemini thought content: '{ThinkingDelta}'", thinkingDelta);
                        }
                        else
                        {
                            textDelta = (textDelta ?? string.Empty) + text; // Append if multiple text parts
                            Logger?.LogTrace("Parsed Gemini text content: '{TextDelta}'", textDelta);
                        }
                    }
                    else if (part.TryGetProperty("functionCall", out var functionCall))
                    {
                        containsFunctionCall = true;
                        string? funcName = null;
                        string? funcArgs = null;
                        if (functionCall.TryGetProperty("name", out var nameElement))
                        {
                            funcName = nameElement.GetString();
                        }
                        if (functionCall.TryGetProperty("args", out var argsElement))
                        {
                            funcArgs = argsElement.GetRawText(); 
                        }

                        if (funcName != null)
                        {
                            toolCallInfo = new ToolCallChunk(0, Name: funcName, ArgumentChunk: funcArgs, IsComplete: true);
                            Logger?.LogDebug("Parsed Gemini function call: {Name}", funcName);
                        }
                    }
                }
            }
        }

        if (root.TryGetProperty("usageMetadata", out var usageMetadata))
        {
            Logger?.LogTrace("Found Gemini usageMetadata: {UsageMetadataRaw}", usageMetadata.GetRawText());
            if (usageMetadata.TryGetProperty("promptTokenCount", out var pToken) && pToken.ValueKind == JsonValueKind.Number)
            {
                inputTokens = pToken.GetInt32();
            }
            if (usageMetadata.TryGetProperty("candidatesTokenCount", out var cToken) && cToken.ValueKind == JsonValueKind.Number)
            {
                outputTokens = cToken.GetInt32();
            }
            else if (usageMetadata.TryGetProperty("totalTokenCount", out var tToken) && tToken.ValueKind == JsonValueKind.Number)
            {
                if (inputTokens.HasValue)
                {
                    outputTokens = tToken.GetInt32() - inputTokens.Value;
                }
                else
                {
                    outputTokens = tToken.GetInt32(); 
                }
            }

            if (inputTokens.HasValue || outputTokens.HasValue)
            {
                Logger?.LogDebug("Parsed Gemini token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
            }
        }
        
        if (containsFunctionCall && finishReason != "tool_calls")
        {
            finishReason = "tool_calls";
            Logger?.LogDebug("Overriding finish reason to 'tool_calls' due to detected functionCall part.");
        }

        Logger?.LogTrace("[GeminiSummary] Processed chunk: TextDelta='{TextDelta}', ThinkingDelta='{ThinkingDelta}', ToolCallName={ToolName}, FinishReason={FR}",
            textDelta,
            thinkingDelta,
            toolCallInfo?.Name,
            finishReason);

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            ThinkingDelta: thinkingDelta,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }
} 