using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Streaming;

public class GrokStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<GrokStreamChunkParser> _logger;
    private int? _lastCompletionTokens = null;

    public GrokStreamChunkParser(ILogger<GrokStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.Grok;

    public ParsedChunkInfo ParseChunk(string rawDataLine)
    {
        if (string.IsNullOrWhiteSpace(rawDataLine) || !rawDataLine.StartsWith("data:"))
        {
            return new ParsedChunkInfo(); // Ignore empty lines or lines not starting with data:
        }

        var jsonData = rawDataLine.Substring(5).Trim(); // Remove "data: " prefix

        if (jsonData == "[DONE]")
        {
            _logger?.LogInformation("Grok stream finished with [DONE] marker.");
            // Reset token tracking for next stream
            var finalOutputTokens = _lastCompletionTokens;
            _lastCompletionTokens = null;
            // Return final output tokens if tracked, otherwise null
            return new ParsedChunkInfo(FinishReason: "stop", OutputTokens: finalOutputTokens);
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            string? textDelta = null;
            int? inputTokens = null;
            int? outputTokensDelta = null; // We'll calculate the delta
            string? finishReason = null;

            if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array && choicesElement.GetArrayLength() > 0)
            {
                var firstChoice = choicesElement[0];
                if (firstChoice.TryGetProperty("delta", out var deltaElement))
                {
                    if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = contentElement.GetString();
                        _logger?.LogTrace("Parsed Grok text delta: '{TextDelta}'", textDelta);
                    }
                }
                 // Grok might also include a finish_reason in the choice, though not shown in basic example
                 if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                 {
                     var reason = reasonElement.GetString();
                     finishReason = reason switch
                     {
                         "stop" => "stop",
                         "length" => "length",
                         "tool_calls" => "tool_calls", // Hypothetical, based on OpenAI/Anthropic
                         _ => reason // Pass through unknown reasons
                     };
                     _logger?.LogInformation("Parsed Grok finish reason: {Reason} (normalized: {Normalized})", reason, finishReason);
                 }
            }

            if (root.TryGetProperty("usage", out var usageElement))
            {
                // Input tokens are usually sent once, capture if available
                if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement) && promptTokensElement.TryGetInt32(out var promptVal))
                {
                    inputTokens = promptVal;
                    _logger?.LogDebug("Parsed Grok input (prompt) tokens: {InputTokens}", inputTokens);
                }

                // Output tokens are cumulative, calculate delta
                if (usageElement.TryGetProperty("completion_tokens", out var completionTokensElement) && completionTokensElement.TryGetInt32(out var currentCompletionVal))
                {
                    if (_lastCompletionTokens.HasValue)
                    {
                        outputTokensDelta = Math.Max(0, currentCompletionVal - _lastCompletionTokens.Value); // Ensure non-negative delta
                    }
                    else
                    {
                        outputTokensDelta = currentCompletionVal; // First time seeing it
                    }
                    _logger?.LogTrace("Parsed Grok cumulative completion tokens: {CumulativeTokens}, Delta: {DeltaTokens}", currentCompletionVal, outputTokensDelta);
                    _lastCompletionTokens = currentCompletionVal; // Update last seen value
                }
            }

            // If a finish reason was found in the choice, reset token tracking
            if (!string.IsNullOrEmpty(finishReason))
            {
                _lastCompletionTokens = null;
            }

            return new ParsedChunkInfo(
                TextDelta: textDelta,
                InputTokens: inputTokens,
                OutputTokens: outputTokensDelta, // Send the calculated delta
                FinishReason: finishReason
            );
        }
        catch (JsonException jsonEx)
        {
            _logger?.LogError(jsonEx, "Failed to parse Grok stream chunk JSON. Raw JSON: {RawJson}", jsonData);
            return new ParsedChunkInfo(FinishReason: "error"); // Indicate an error
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error parsing Grok stream chunk. Raw JSON: {RawJson}", jsonData);
            return new ParsedChunkInfo(FinishReason: "error"); // Indicate an error
        }
    }
} 