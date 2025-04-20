using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Streaming;

public class GeminiStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<GeminiStreamChunkParser> _logger;

    public GeminiStreamChunkParser(ILogger<GeminiStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.Gemini;

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            ToolCallChunk? toolCallInfo = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null;
            bool containsFunctionCall = false;

            _logger?.LogInformation("[GeminiDebug] Raw chunk content: {RawContent}", rawJson);

            if (root.TryGetProperty("error", out var errorElement))
            {
                _logger?.LogError("Gemini stream reported error: {ErrorJson}", errorElement.GetRawText());
                return new ParsedChunkInfo(FinishReason: "error");
            }

            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];

                _logger?.LogInformation("[GeminiDebug] Candidate content: {Candidate}", firstCandidate.GetRawText());

                if (firstCandidate.TryGetProperty("finishReason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                {
                    var reason = reasonElement.GetString()?.ToUpperInvariant();
                    _logger?.LogInformation("Found Gemini finishReason: {Reason}", reason);

                    switch (reason)
                    {
                        case "STOP":
                            finishReason = "stop";
                            break;
                        case "MAX_TOKENS":
                            finishReason = "length";
                            break;
                        case "SAFETY":
                            finishReason = "content_filter";
                            break;
                        case "RECITATION":
                            finishReason = "content_filter";
                            break;
                        case "TOOL_CALLS":
                        case "FUNCTION_CALL":
                            finishReason = "tool_calls";
                            break;
                        case null:
                            break;
                        default:
                            _logger?.LogWarning("Unknown Gemini finish reason: {Reason}", reason);
                            break;
                    }
                }

                if (firstCandidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                        {
                            textDelta = textElement.GetString();
                            _logger?.LogInformation("Parsed Gemini text content: '{TextDelta}'", textDelta);
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
                                toolCallInfo = new ToolCallChunk(0, Id: Guid.NewGuid().ToString(), funcName, funcArgs);
                                _logger?.LogInformation("Parsed Gemini function call: {Name}", funcName);
                            }
                        }
                    }
                }
            }

            if (root.TryGetProperty("usageMetadata", out var usageMetadata))
            {
                _logger?.LogTrace("Found Gemini usageMetadata.");
                if (usageMetadata.TryGetProperty("promptTokenCount", out var pToken) && pToken.ValueKind == JsonValueKind.Number)
                {
                    inputTokens = pToken.GetInt32();
                }
                if (usageMetadata.TryGetProperty("candidatesTokenCount", out var cToken) && cToken.ValueKind == JsonValueKind.Number)
                {
                    outputTokens = cToken.GetInt32();
                }
                else if (usageMetadata.TryGetProperty("totalTokenCount", out var tToken) && tToken.ValueKind == JsonValueKind.Number && inputTokens.HasValue)
                {
                    outputTokens = tToken.GetInt32() - inputTokens.Value;
                }

                if (inputTokens.HasValue || outputTokens.HasValue)
                {
                    _logger?.LogDebug("Parsed Gemini token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
                }
            }

            if (containsFunctionCall)
            {
                finishReason = "tool_calls";
                _logger?.LogInformation("Setting finish reason to 'tool_calls' due to function call presence");
            }

            if (!string.IsNullOrEmpty(textDelta) && string.IsNullOrEmpty(finishReason) && root.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean())
            {
                finishReason = "stop";
                _logger?.LogInformation("Setting finish reason to 'stop' for final content chunk");
            }

            _logger?.LogInformation("[GeminiSummary] Processed chunk: TextDelta={TextDelta}, ToolCallInfo={ToolCallInfo}, FinishReason={FinishReason}",
                textDelta != null ? $"Length: {textDelta.Length}" : "null",
                toolCallInfo != null ? $"Name: {toolCallInfo.Name}" : "null",
                finishReason);

            return new ParsedChunkInfo(
                TextDelta: textDelta,
                ToolCallInfo: toolCallInfo,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                FinishReason: finishReason
            );
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse Gemini stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing Gemini stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
    }
} 