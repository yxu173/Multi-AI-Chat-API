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

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var rawJson = jsonDoc.RootElement.GetRawText();
        Logger.LogTrace("[AnthropicParser] Received raw data chunk: {RawContent}", rawJson);

        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            Logger.LogWarning("Anthropic stream chunk missing or invalid 'type' property: {RawJson}", rawJson);
            return new ParsedChunkInfo(); 
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
                        else if (deltaType.GetString() == "input_json_delta")
                        {
                            string? argumentChunk = deltaElement.GetProperty("partial_json").GetString();
                            toolCallInfo = new ToolCallChunk(index, ArgumentChunk: argumentChunk);
                            Logger.LogTrace("Anthropic tool use delta (args): {ArgumentChunk} for index {ToolIndex}", argumentChunk, index);
                        }
                    }
                }
                break;

            case "content_block_stop":
                if (root.TryGetProperty("index", out var blockIndexElement))
                {
                    int stoppedBlockIndex = blockIndexElement.GetInt32();
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
                Logger.LogInformation("Anthropic message_stop event received.");
                if (root.TryGetProperty("amazon-bedrock-invocationMetrics", out var bedrockMetrics))
                {
                    inputTokens = bedrockMetrics.GetProperty("inputTokenCount").GetInt32();
                    outputTokens = bedrockMetrics.GetProperty("outputTokenCount").GetInt32();
                }
                else if (root.TryGetProperty("usage", out var finalUsage)) 
                {
                     if (inputTokens == null && finalUsage.TryGetProperty("input_tokens", out var finalInTokens)) inputTokens = finalInTokens.GetInt32();
                     if (finalUsage.TryGetProperty("output_tokens", out var finalOutTokens)) outputTokens = finalOutTokens.GetInt32();
                }
                
                if (string.IsNullOrEmpty(finishReason))
                {
                    finishReason = "stop"; 
                }
                break;

            case "ping":
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