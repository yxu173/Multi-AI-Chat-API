using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Streaming;

public class DeepseekStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<DeepseekStreamChunkParser> _logger;

    public DeepseekStreamChunkParser(ILogger<DeepseekStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.DeepSeek;

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            string? thinkingDelta = null;
            ToolCallChunk? toolCallInfo = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null;

            _logger?.LogInformation("[DeepseekDebug] Raw chunk content: {RawContent}", rawJson);

            if (root.TryGetProperty("error", out var errorElement))
            {
                _logger?.LogError("Deepseek stream reported error: {ErrorJson}", errorElement.GetRawText());
                return new ParsedChunkInfo(FinishReason: "error");
            }

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];

                if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                {
                    finishReason = reasonElement.GetString();
                    _logger?.LogInformation("Parsed Deepseek finishReason: {FinishReason}", finishReason);
                }

                if (firstChoice.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = contentElement.GetString();
                        _logger?.LogInformation("Parsed Deepseek text delta: '{TextDelta}'", textDelta);
                    }

                    if (delta.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                    {
                        thinkingDelta = reasoningElement.GetString();
                        _logger?.LogInformation("Parsed Deepseek thinking delta: '{ThinkingDelta}'", thinkingDelta);
                    }

                    if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                    {
                        var firstToolCall = toolCalls[0];
                        string? toolCallId = null;
                        string? toolCallName = null;
                        string? toolCallArgs = null;

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
                                toolCallArgs = argsElement.GetString();
                            }
                        }

                        if (toolCallId != null || toolCallName != null)
                        {
                            toolCallInfo = new ToolCallChunk(0, toolCallId, toolCallName, toolCallArgs);
                            _logger?.LogInformation("Parsed Deepseek tool call. Id: {Id}, Name: {Name}", toolCallId, toolCallName);
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
                    _logger?.LogDebug("Parsed Deepseek token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse Deepseek stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing Deepseek stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
    }
} 