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

    protected override ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        Logger?.LogInformation("[DeepseekDebug] Raw chunk content: {RawContent}", rawJson);

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
                // DeepSeek finish reasons: "stop", "length", "tool_calls"
                Logger?.LogInformation("Parsed Deepseek finishReason: {FinishReason}", finishReason);
            }

            if (firstChoice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                {
                    textDelta = contentElement.GetString();
                    Logger?.LogTrace("Parsed Deepseek text delta: '{TextDelta}'", textDelta);
                }

                // Deepseek might use a specific field for "thinking" or it might be part of system messages/prompts not in stream delta.
                // The provided original code checks for "reasoning_content", let's assume this is their thinking indicator if present.
                if (delta.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                {
                    thinkingDelta = reasoningElement.GetString();
                    Logger?.LogTrace("Parsed Deepseek thinking delta: '{ThinkingDelta}'", thinkingDelta);
                }

                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                {
                    // Assuming Deepseek tool calls in delta are similar to OpenAI's, usually one per relevant chunk part.
                    var firstToolCall = toolCalls[0]; // Process the first tool call in the array if multiple are sent (unlikely for delta)
                    string? toolCallId = null;
                    string? toolCallName = null;
                    string? toolCallArgsChunk = null; // Argument part for this chunk
                    bool isToolCallComplete = finishReason == "tool_calls"; // If finish_reason is tool_calls, this specific call part is for a completed call object.

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
                            // Arguments might be a partial string or a full JSON string if the tool call is complete in this chunk.
                            toolCallArgsChunk = argsElement.GetString(); 
                        }
                    }

                    if (toolCallId != null || toolCallName != null)
                    {
                        // Deepseek might not provide an explicit index in the same way as OpenAI/Anthropic for chunked tool calls.
                        // Using a default index of 0, assuming StreamProcessor can handle re-assembly if needed.
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