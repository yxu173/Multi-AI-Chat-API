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

    protected override ParsedChunkInfo ParseModelSpecificChunkWithReader(ref Utf8JsonReader reader)
    {
        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        string? type = null;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Read();
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "type":
                    type = reader.GetString();
                    break;
                case "delta":
                    ParseDeltaObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
                    break;
                case "usage":
                    ParseUsageObject(ref reader, ref inputTokens, ref outputTokens);
                    break;
                case "stop_reason":
                    finishReason = reader.GetString();
                    break;
                case "content":
                    ParseContentArray(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        // Map Anthropic stop reasons to standard finish reasons
        finishReason = finishReason switch
        {
            "end_turn" => "stop",
            "max_tokens" => "length",
            "stop_sequence" => "stop",
            _ => finishReason
        };

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            ThinkingDelta: thinkingDelta,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }

    private void ParseDeltaObject(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "type":
                    var deltaType = reader.GetString();
                    break;
                case "text":
                    textDelta = reader.GetString();
                    break;
                case "thinking":
                    thinkingDelta = reader.GetString();
                    break;
                case "tool_use":
                    ParseToolUseObject(ref reader, ref toolCallInfo);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private void ParseContentArray(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            ParseContentObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
        }
    }

    private void ParseContentObject(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo)
    {
        string? contentType = null;
        string? contentText = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "type":
                    contentType = reader.GetString();
                    break;
                case "text":
                    contentText = reader.GetString();
                    break;
                case "tool_use":
                    ParseToolUseObject(ref reader, ref toolCallInfo);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (contentType == "text" && !string.IsNullOrEmpty(contentText))
        {
            textDelta = contentText;
        }
    }

    private void ParseToolUseObject(ref Utf8JsonReader reader, ref ToolCallChunk? toolCallInfo)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        string? toolId = null;
        string? toolName = null;
        string? toolArguments = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "id":
                    toolId = reader.GetString();
                    break;
                case "name":
                    toolName = reader.GetString();
                    break;
                case "input":
                    toolArguments = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (!string.IsNullOrEmpty(toolName))
        {
            toolCallInfo = new ToolCallChunk(0, toolId, toolName, toolArguments);
        }
    }

    private void ParseUsageObject(ref Utf8JsonReader reader, ref int? inputTokens, ref int? outputTokens)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "input_tokens":
                    inputTokens = reader.GetInt32();
                    break;
                case "output_tokens":
                    outputTokens = reader.GetInt32();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            Logger?.LogWarning("Anthropic stream chunk missing or invalid 'type' property");
            return new ParsedChunkInfo();
        }

        var eventType = typeElement.GetString();
        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        switch (eventType)
        {
            case "content_block_delta":
                if (root.TryGetProperty("delta", out var deltaElement))
                {
                    if (deltaElement.TryGetProperty("type", out var deltaType) && deltaType.GetString() == "text_delta")
                    {
                        if (deltaElement.TryGetProperty("text", out var textElement))
                        {
                            textDelta = textElement.GetString();
                            Logger?.LogTrace("Parsed Anthropic text delta: '{TextDelta}'", textDelta);
                        }
                    }
                }
                break;

            case "message_delta":
                if (root.TryGetProperty("delta", out var messageDelta))
                {
                    if (messageDelta.TryGetProperty("thinking", out var thinkingElement))
                    {
                        thinkingDelta = thinkingElement.GetString();
                        Logger?.LogTrace("Parsed Anthropic thinking delta: '{ThinkingDelta}'", thinkingDelta);
                    }
                }
                break;

            case "message_stop":
                Logger?.LogInformation("Received Anthropic message stop event");
                finishReason = "stop";
                break;

            case "content_block_stop":
                Logger?.LogInformation("Received Anthropic content block stop event");
                break;

            case "message":
                if (root.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("input_tokens", out var promptTokens)) inputTokens = promptTokens.GetInt32();
                    if (usage.TryGetProperty("output_tokens", out var completionTokens)) outputTokens = completionTokens.GetInt32();
                    Logger?.LogDebug("Parsed Anthropic token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
                }

                if (root.TryGetProperty("stop_reason", out var stopReasonElement))
                {
                    var stopReason = stopReasonElement.GetString();
                    finishReason = stopReason switch
                    {
                        "end_turn" => "stop",
                        "max_tokens" => "length",
                        "stop_sequence" => "stop",
                        _ => stopReason
                    };
                    Logger?.LogInformation("Parsed Anthropic stop reason: {StopReason}, mapped to: {FinishReason}", stopReason, finishReason);
                }
                break;

            default:
                Logger?.LogTrace("Unhandled Anthropic event type: {EventType}", eventType);
                break;
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
} 