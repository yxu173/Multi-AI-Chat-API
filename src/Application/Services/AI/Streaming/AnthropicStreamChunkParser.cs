using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class AnthropicStreamChunkParser : BaseStreamChunkParser<AnthropicStreamChunkParser>
{
    public AnthropicStreamChunkParser(ILogger<AnthropicStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.Anthropic;

    protected override ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        Logger.LogTrace("[AnthropicParser] Received raw data chunk: {RawContent}", rawJson);

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            Logger.LogWarning("Anthropic stream chunk missing or invalid 'type' property: {RawJson}", rawJson);
            return new ParsedChunkInfo(); // Or a specific error
        }

        var eventType = typeElement.GetString();
        string? textDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        switch (eventType)
        {
            case "message_start":
                if (root.TryGetProperty("message", out var messageElement))
                {
                    if (messageElement.TryGetProperty("usage", out var usageElement))
                    {
                        if (usageElement.TryGetProperty("input_tokens", out var inTokens))
                        {
                            inputTokens = inTokens.GetInt32();
                        }
                    }
                }
                break;

            case "content_block_start":
                if (root.TryGetProperty("content_block", out var contentBlockStart))
                {
                    if (contentBlockStart.TryGetProperty("type", out var cBlockType) && cBlockType.GetString() == "tool_use")
                    {
                        int index = root.GetProperty("index").GetInt32();
                        string? id = contentBlockStart.GetProperty("id").GetString();
                        string? name = contentBlockStart.GetProperty("name").GetString();
                        // Input is not available here, it will be in content_block_delta for tool_use
                        toolCallInfo = new ToolCallChunk(index, id, name, null, false);
                        Logger.LogDebug("Anthropic tool use started: {ToolName} (ID: {ToolId}, Index: {ToolIndex})", name, id, index);
                    }
                }
                break;

            case "content_block_delta":
                if (root.TryGetProperty("delta", out var deltaElement))
                {
                    var index = root.GetProperty("index").GetInt32();
                    if (deltaElement.TryGetProperty("type", out var deltaType))
                    {
                        if (deltaType.GetString() == "text_delta")
                        {
                            textDelta = deltaElement.GetProperty("text").GetString();
                        }
                        else if (deltaType.GetString() == "input_json_delta") // For tool use arguments
                        {
                            string? argumentChunk = deltaElement.GetProperty("partial_json").GetString();
                            toolCallInfo = new ToolCallChunk(index, ArgumentChunk: argumentChunk);
                            Logger.LogTrace("Anthropic tool use delta (args): {ArgumentChunk} for index {ToolIndex}", argumentChunk, index);
                        }
                    }
                }
                break;

            case "content_block_stop":
                // This event indicates a content block (like a tool_use block) has finished streaming its content.
                // For tool calls, this means the arguments are complete.
                if (root.TryGetProperty("index", out var blockIndexElement))
                {
                    int stoppedBlockIndex = blockIndexElement.GetInt32();
                    // We can mark the tool call at this index as having its arguments complete.
                    // The overall `tool_calls` finish reason comes later in `message_delta` or `message_stop`.
                    // However, the `StreamProcessor` currently reconstructs tool calls. This might need adjustment
                    // if StreamProcessor expects IsComplete on a per-chunk basis to mean the *entire* tool call object is done.
                    // For Anthropic, argument streaming is done, but the overall decision to use tools might not be final yet.
                    // Let's assume IsComplete on ToolCallChunk means arguments for that specific call are done streaming.
                    toolCallInfo = new ToolCallChunk(stoppedBlockIndex, IsComplete: true); 
                    Logger.LogDebug("Anthropic content_block_stop for index {Index}", stoppedBlockIndex);
                }
                break;

            case "message_delta":
                if (root.TryGetProperty("delta", out var messageDeltaElement))
                {
                    if (messageDeltaElement.TryGetProperty("stop_reason", out var stopReasonElement) && stopReasonElement.ValueKind != JsonValueKind.Null)
                    {
                        finishReason = stopReasonElement.GetString();
                        Logger.LogInformation("Anthropic message_delta stop_reason: {StopReason}", finishReason);

                        if (finishReason == "tool_use")
                        {
                             // This indicates the model wants to use tools. The actual tool details were in content_block events.
                             // The StreamProcessor will collect these tool calls.
                        }
                    }
                }
                if (root.TryGetProperty("usage", out var messageDeltaUsage) && messageDeltaUsage.TryGetProperty("output_tokens", out var outTokensDelta))
                {
                    outputTokens = outTokensDelta.GetInt32();
                }
                break;

            case "message_stop":
                // This is the final event in the stream.
                // It might contain the final output_tokens if not already provided by message_delta.
                // It also confirms the stop_reason.
                Logger.LogInformation("Anthropic message_stop event received.");
                if (root.TryGetProperty("amazon-bedrock-invocationMetrics", out var bedrockMetrics))
                {
                    inputTokens = bedrockMetrics.GetProperty("inputTokenCount").GetInt32();
                    outputTokens = bedrockMetrics.GetProperty("outputTokenCount").GetInt32();
                }
                else if (root.TryGetProperty("usage", out var finalUsage)) // Check for Claude directly
                {
                     if (inputTokens == null && finalUsage.TryGetProperty("input_tokens", out var finalInTokens)) inputTokens = finalInTokens.GetInt32();
                     if (finalUsage.TryGetProperty("output_tokens", out var finalOutTokens)) outputTokens = finalOutTokens.GetInt32();
                }
                
                // The actual finish_reason should have been set by message_delta if it was a natural stop or tool_use.
                // If finish_reason is still null here, it implies a successful completion (e.g. "stop_sequence" or "max_tokens").
                // The StreamProcessor seems to handle the case where finish_reason is "tool_calls" or "stop".
                // Anthropic uses "tool_use", "stop_sequence", "max_tokens", "end_turn".
                // We need to map these to what StreamProcessor expects or ensure StreamProcessor handles them.
                // For now, pass it through. If it was already set by message_delta, this won't overwrite unless it was null.
                // If not set by message_delta, and we reach message_stop, assume it's a natural stop.
                if (string.IsNullOrEmpty(finishReason))
                {
                    finishReason = "stop"; // Default to "stop" if no explicit reason from delta and stream ends
                }
                break;

            case "ping":
                // Keep-alive event, ignore.
                break;

            default:
                Logger.LogWarning("Unhandled Anthropic event type: {EventType} - {RawJson}", eventType, rawJson);
                break;
        }

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }
} 