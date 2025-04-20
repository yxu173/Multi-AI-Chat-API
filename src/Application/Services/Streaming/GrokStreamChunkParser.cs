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
        // Grok uses the 'data: [DONE]' marker *outside* the JSON payload.
        // The ReadStreamAsync method in the service layer filters this out.
        // So, we only expect JSON objects here.
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "[DONE]")
        {
            _logger?.LogTrace("[GrokParser] Received empty or DONE marker, indicating end of stream (handled upstream).");
            // Return empty chunk; stream termination is handled by StreamProcessor
            return new ParsedChunkInfo(); 
        }

        try
        {
            _logger?.LogTrace("[GrokParser] Received raw data chunk: {RawContent}", rawJson);

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null; // Grok doesn't explicitly send finish_reason in delta chunks AFAIK
            ToolCallChunk? toolCallInfo = null; // Grok doesn't support tools in the example

            // Extract Text Delta
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
                    // Check for finish reason in choice (though not standard in Grok examples)
                    if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                    { 
                        finishReason = reasonElement.GetString();
                        _logger?.LogDebug("Parsed Grok finish reason from choice: {FinishReason}", finishReason);
                    }
                }
            }

            // Extract Token Usage (present in each chunk in the example)
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
                 _logger?.LogTrace("Parsed Grok token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
            }

            // If we received content, it's not the final signal chunk.
            // If no content and no explicit finish reason, it might be the final chunk before [DONE]
            // or an empty chunk. The StreamProcessor will handle stream termination.
            if (finishReason == null && textDelta == null)
            {
                 _logger?.LogTrace("Grok chunk has no text delta or explicit finish reason. Might be final data chunk before [DONE].");
            }
            
            // Map Grok's implicit completion (end of stream) to a reason if needed, 
            // but typically handled by the caller observing the stream end.
            // Let's rely on the StreamProcessor detecting the end for now.

            return new ParsedChunkInfo(
                TextDelta: textDelta,
                ToolCallInfo: toolCallInfo, // Null for Grok currently
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                FinishReason: finishReason // Usually null for Grok chunks, completion inferred by stream end
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