using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Streaming;

public class GrokStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<GrokStreamChunkParser> _logger;

    public GrokStreamChunkParser(ILogger<GrokStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.Grok;

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "[DONE]")
        {
            _logger?.LogTrace("[GrokParser] Received empty or DONE marker, indicating end of stream (handled upstream).");
            return new ParsedChunkInfo(); 
        }

        try
        {
            _logger?.LogTrace("[GrokParser] Received raw data chunk: {RawContent}", rawJson);

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            string? thinkingDelta = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null; 
            ToolCallChunk? toolCallInfo = null; 

            if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array && choicesElement.GetArrayLength() > 0)
            {
                var firstChoice = choicesElement[0];
                
                if (firstChoice.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.Object)
                {
                    if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = contentElement.GetString();
                        _logger?.LogTrace("Parsed Grok text delta: '{TextDelta}'", textDelta);
                    }
                    
                    if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                    { 
                        finishReason = reasonElement.GetString();
                        _logger?.LogDebug("Parsed Grok finish reason from choice: {FinishReason}", finishReason);
                    }
                }
                
                if (deltaElement.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                {
                    thinkingDelta = reasoningElement.GetString();
                    _logger?.LogTrace("Parsed Grok reasoning content: '{ReasoningContent}'", thinkingDelta);
                }
            }

            // Extract Token Usage
            if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
            {
                if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement) && promptTokensElement.TryGetInt32(out var pt))
                {
                    inputTokens = pt;
                }
                if (usageElement.TryGetProperty("completion_tokens", out var completionTokensElement) && completionTokensElement.TryGetInt32(out var ct))
                {
                    outputTokens = ct;
                }
                
                // Try to get reasoning tokens as well
                if (usageElement.TryGetProperty("reasoning_tokens", out var reasoningTokensElement) && reasoningTokensElement.TryGetInt32(out var rt))
                {
                    _logger?.LogTrace("Found reasoning tokens: {ReasoningTokens}", rt);
                    // We could add these tokens to the output tokens if needed
                }
                
                _logger?.LogTrace("Parsed Grok token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
            }

            // If we received content, it's not the final signal chunk.
            if (finishReason == null && textDelta == null && thinkingDelta == null)
            {
                _logger?.LogTrace("Grok chunk has no text delta, reasoning, or explicit finish reason. Might be final data chunk before [DONE].");
            }

            return new ParsedChunkInfo(
                TextDelta: textDelta,
                ThinkingDelta: thinkingDelta,
                ToolCallInfo: toolCallInfo,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                FinishReason: finishReason
            );
        }
        catch (JsonException jsonEx)
        {    
            _logger?.LogError(jsonEx, "Failed to parse Grok stream chunk JSON. RawChunk: {RawChunk}", rawJson);
            // Signal error to the processor
            return new ParsedChunkInfo(FinishReason: "error"); 
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error parsing Grok stream chunk. RawChunk: {RawChunk}", rawJson);
            // Signal error to the processor
            return new ParsedChunkInfo(FinishReason: "error");
        }
    }
} 