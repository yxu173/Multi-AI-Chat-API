using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Streaming;

public class OpenAiStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<OpenAiStreamChunkParser> _logger;

    public OpenAiStreamChunkParser(ILogger<OpenAiStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.OpenAi;

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            ToolCallChunk? toolCallInfo = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null;

            _logger?.LogInformation("[OpenAiDebug] Raw chunk content: {RawContent}", rawJson);

            if (root.TryGetProperty("error", out var errorElement))
            {
                _logger?.LogError("OpenAI stream reported error: {ErrorJson}", errorElement.GetRawText());
                return new ParsedChunkInfo(FinishReason: "error");
            }

            // Check for [DONE] message which OpenAI sends as a plain string
            if (rawJson.Trim() == "[DONE]")
            {
                _logger?.LogInformation("Received OpenAI [DONE] message");
                return new ParsedChunkInfo(FinishReason: "stop");
            }

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];

                if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                {
                    finishReason = reasonElement.GetString();
                    _logger?.LogInformation("Parsed OpenAI finishReason: {FinishReason}", finishReason);
                }

                if (firstChoice.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = contentElement.GetString();
                        _logger?.LogInformation("Parsed OpenAI text delta: '{TextDelta}'", textDelta);
                    }

                    if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                    {
                        var firstToolCall = toolCalls[0];
                        string? toolCallId = null;
                        string? toolCallName = null;
                        string? argumentChunk = null;
                        int toolCallIndex = 0;

                        if (firstToolCall.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out int index))
                        {
                            toolCallIndex = index;
                        }

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
                                argumentChunk = argsElement.GetString();
                            }
                        }

                        toolCallInfo = new ToolCallChunk(toolCallIndex, toolCallId, toolCallName, ArgumentChunk: argumentChunk);

                        if (!string.IsNullOrEmpty(toolCallId) || !string.IsNullOrEmpty(toolCallName))
                        {
                            _logger?.LogInformation("Parsed OpenAI tool call start/info. Index: {Index}, Id: {Id}, Name: {Name}", toolCallIndex, toolCallId, toolCallName);
                        }
                        if (!string.IsNullOrEmpty(argumentChunk))
                        {
                            _logger?.LogInformation("Parsed OpenAI tool call argument chunk. Index: {Index}, Length: {Length}", toolCallIndex, argumentChunk.Length);
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
                    _logger?.LogDebug("Parsed OpenAI token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
                }
            }

            // If we have a finish_reason but no text content, this is likely a completion signal
            if (!string.IsNullOrEmpty(finishReason) && string.IsNullOrEmpty(textDelta) && toolCallInfo == null)
            {
                _logger?.LogInformation("Detected completion signal with finish_reason: {FinishReason}", finishReason);
            }

            return new ParsedChunkInfo(
                TextDelta: textDelta,
                ToolCallInfo: toolCallInfo,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                FinishReason: finishReason
            );
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse OpenAI stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing OpenAI stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
    }
} 