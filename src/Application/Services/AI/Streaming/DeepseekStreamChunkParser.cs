using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class DeepseekStreamChunkParser : BaseStreamChunkParser<DeepseekStreamChunkParser>
{
    public DeepseekStreamChunkParser(ILogger<DeepseekStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.DeepSeek;

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        Logger?.LogInformation("[DeepseekDebug] Raw chunk content: {RawContent}", jsonDoc.RootElement.GetRawText());

        if (root.TryGetProperty("error", out var errorElement))
        {
            Logger?.LogError("Deepseek stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error");
        }

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];

            if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = reasonElement.GetString();
                Logger?.LogInformation("Parsed Deepseek finishReason: {FinishReason}", finishReason);
            }

            if (firstChoice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                {
                    textDelta = contentElement.GetString();
                    Logger?.LogTrace("Parsed Deepseek text delta: '{TextDelta}'", textDelta);
                }
                if (delta.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                {
                    thinkingDelta = reasoningElement.GetString();
                    Logger?.LogTrace("Parsed Deepseek thinking delta: '{ThinkingDelta}'", thinkingDelta);
                }

                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                {
                    var firstToolCall = toolCalls[0]; 
                    string? toolCallId = null;
                    string? toolCallName = null;
                    string? toolCallArgsChunk = null; 
                    bool isToolCallComplete = finishReason == "tool_calls"; 

                    if (firstToolCall.TryGetProperty("id", out var idElement))
                    {
                        toolCallId = idElement.GetString();
                    }
                    if (firstToolCall.TryGetProperty("function", out var function))
                    {
                        if (function.TryGetProperty("name", out var nameElement))
                        {
                            toolCallName = nameElement.GetString();
                        }
                        if (function.TryGetProperty("arguments", out var argsElement))
                        {
                            toolCallArgsChunk = argsElement.GetString(); 
                        }
                    }

                    if (toolCallId != null || toolCallName != null)
                    {
                        toolCallInfo = new ToolCallChunk(0, toolCallId, toolCallName, toolCallArgsChunk, isToolCallComplete);
                        Logger?.LogDebug("Parsed Deepseek tool call. Id: {Id}, Name: {Name}, ArgsChunk: {HasArgs}", toolCallId, toolCallName, !string.IsNullOrEmpty(toolCallArgsChunk));
                    }
                }
            }
        }

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
            {
                inputTokens = promptTokens.GetInt32();
            }
            if (usage.TryGetProperty("completion_tokens", out var completionTokens))
            {
                outputTokens = completionTokens.GetInt32();
            }
            if (inputTokens.HasValue || outputTokens.HasValue)
            {
                Logger?.LogDebug("Parsed Deepseek token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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
} 