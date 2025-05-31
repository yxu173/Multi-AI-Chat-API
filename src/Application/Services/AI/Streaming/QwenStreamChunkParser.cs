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

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        string? textDelta = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        ToolCallChunk? toolCallInfo = null;

        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind != JsonValueKind.Null)
        {
            if (usageElement.TryGetProperty("prompt_tokens", out var promptTokens))
            {
                inputTokens = promptTokens.GetInt32();
            }

            if (usageElement.TryGetProperty("completion_tokens", out var completionTokens))
            {
                outputTokens = completionTokens.GetInt32();
            }
        }

        if (root.TryGetProperty("choices", out var choicesElement) &&
            choicesElement.ValueKind == JsonValueKind.Array &&
            choicesElement.GetArrayLength() > 0)
        {
            var firstChoice = choicesElement[0];

            if (firstChoice.TryGetProperty("finish_reason", out var finishReasonElement) &&
                finishReasonElement.ValueKind != JsonValueKind.Null)
            {
                finishReason = finishReasonElement.GetString();
            }

            if (firstChoice.TryGetProperty("delta", out var deltaElement))
            {
                if (deltaElement.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind != JsonValueKind.Null)
                {
                    textDelta = contentElement.GetString();
                }

                if (deltaElement.TryGetProperty("tool_calls", out var toolCallsElement) &&
                    toolCallsElement.ValueKind == JsonValueKind.Array &&
                    toolCallsElement.GetArrayLength() > 0)
                {
                    var toolCall = toolCallsElement[0];
                    int index = 0;
                    string? id = null;
                    string? name = null;
                    string? argumentChunk = null;
                    bool isComplete = false;

                    if (toolCall.TryGetProperty("id", out var idElement) &&
                        idElement.ValueKind != JsonValueKind.Null)
                    {
                        id = idElement.GetString();
                    }

                    if (toolCall.TryGetProperty("function", out var functionElement))
                    {
                        if (functionElement.TryGetProperty("name", out var nameElement) &&
                            nameElement.ValueKind != JsonValueKind.Null)
                        {
                            name = nameElement.GetString();
                        }

                        if (functionElement.TryGetProperty("arguments", out var argumentsElement) &&
                            argumentsElement.ValueKind != JsonValueKind.Null)
                        {
                            argumentChunk = argumentsElement.GetString();
                        }
                    }

                    if (finishReason == "tool_calls")
                    {
                        isComplete = true;
                    }

                    toolCallInfo = new ToolCallChunk(index, id, name, argumentChunk, isComplete);
                }
            }
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