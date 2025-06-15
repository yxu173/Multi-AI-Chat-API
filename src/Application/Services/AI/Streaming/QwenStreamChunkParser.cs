using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class QwenStreamChunkParser : BaseStreamChunkParser<QwenStreamChunkParser>
{
    public QwenStreamChunkParser(ILogger<QwenStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.Qwen;

    protected override ParsedChunkInfo ParseModelSpecificChunkWithReader(ref Utf8JsonReader reader)
    {
        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

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
                case "id":
                case "object":
                case "created":
                case "model":
                    reader.Skip();
                    break;
                case "choices":
                    ParseChoicesArray(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo, ref finishReason);
                    break;
                case "usage":
                    ParseUsageObject(ref reader, ref inputTokens, ref outputTokens);
                    break;
                default:
                    reader.Skip();
                    break;
            }
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

    private void ParseChoicesArray(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo, ref string? finishReason)
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

            ParseChoiceObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo, ref finishReason);
        }
    }

    private void ParseChoiceObject(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo, ref string? finishReason)
    {
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
                case "index":
                    reader.Skip();
                    break;
                case "delta":
                    ParseDeltaObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
                    break;
                case "finish_reason":
                    finishReason = reader.GetString();
                    break;
                case "logprobs":
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
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
                case "role":
                    reader.Skip();
                    break;
                case "content":
                    textDelta = reader.GetString();
                    break;
                case "tool_calls":
                    ParseToolCallsArray(ref reader, ref toolCallInfo);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private void ParseToolCallsArray(ref Utf8JsonReader reader, ref ToolCallChunk? toolCallInfo)
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

            ParseToolCallObject(ref reader, ref toolCallInfo);
        }
    }

    private void ParseToolCallObject(ref Utf8JsonReader reader, ref ToolCallChunk? toolCallInfo)
    {
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
                case "index":
                    reader.Skip();
                    break;
                case "id":
                    toolId = reader.GetString();
                    break;
                case "type":
                    reader.Skip();
                    break;
                case "function":
                    ParseFunctionObject(ref reader, ref toolName, ref toolArguments);
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

    private void ParseFunctionObject(ref Utf8JsonReader reader, ref string? toolName, ref string? toolArguments)
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
                case "name":
                    toolName = reader.GetString();
                    break;
                case "arguments":
                    toolArguments = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
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
                case "prompt_tokens":
                    inputTokens = reader.GetInt32();
                    break;
                case "completion_tokens":
                    outputTokens = reader.GetInt32();
                    break;
                case "total_tokens":
                    reader.Skip();
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

        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        // Parse choices array
        if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choicesElement.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var deltaElement))
                {
                    if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = contentElement.GetString();
                        Logger?.LogTrace("Parsed Qwen text delta: '{TextDelta}'", textDelta);
                    }
                    else if (deltaElement.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var toolCall in toolCallsElement.EnumerateArray())
                        {
                            if (toolCall.TryGetProperty("function", out var functionElement))
                            {
                                if (functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                                {
                                    var functionName = nameElement.GetString();
                                    string? functionArgs = null;
                                    if (functionElement.TryGetProperty("arguments", out var argsElement))
                                    {
                                        functionArgs = argsElement.GetString();
                                    }
                                    toolCallInfo = new ToolCallChunk(0, null, functionName, functionArgs);
                                    Logger?.LogTrace("Parsed Qwen function call: {FunctionName}", functionName);
                                }
                            }
                        }
                    }
                }

                if (choice.TryGetProperty("finish_reason", out var finishReasonElement) && finishReasonElement.ValueKind == JsonValueKind.String)
                {
                    finishReason = finishReasonElement.GetString();
                    Logger?.LogDebug("Parsed Qwen finish reason: {FinishReason}", finishReason);
                }
            }
        }

        // Parse usage
        if (root.TryGetProperty("usage", out var usageElement))
        {
            if (usageElement.TryGetProperty("prompt_tokens", out var promptTokens)) inputTokens = promptTokens.GetInt32();
            if (usageElement.TryGetProperty("completion_tokens", out var completionTokens)) outputTokens = completionTokens.GetInt32();
            Logger?.LogDebug("Parsed Qwen token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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