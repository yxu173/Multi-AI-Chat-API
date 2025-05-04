using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class AnthropicStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<AnthropicStreamChunkParser> _logger;

    public AnthropicStreamChunkParser(ILogger<AnthropicStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.Anthropic;

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

            _logger?.LogInformation("[AnthropicDebug] Raw chunk content: {RawContent}", rawJson);

            // Handle error responses first
            if (root.TryGetProperty("error", out var errorElement))
            {
                string errorType = string.Empty;
                string errorMessage = string.Empty;

                if (errorElement.TryGetProperty("type", out var typeElement))
                {
                    errorType = typeElement.GetString() ?? string.Empty;
                }
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    errorMessage = messageElement.GetString() ?? string.Empty;
                }

                _logger?.LogError("Anthropic stream reported error: {ErrorJson}", errorElement.GetRawText());

                finishReason = errorType switch
                {
                    "overloaded_error" => "overloaded",
                    "rate_limit_error" => "rate_limit",
                    "invalid_request_error" => "invalid_request",
                    _ => "error"
                };

                return new ParsedChunkInfo(FinishReason: finishReason);
            }

            // Main event processing
            if (root.TryGetProperty("type", out var element) && element.ValueKind == JsonValueKind.String)
            {
                var eventType = element.GetString();
                _logger?.LogDebug("Processing Anthropic event type: {EventType}", eventType);

                switch (eventType)
                {
                    case "message_start":
                        if (root.TryGetProperty("message", out var messageStart) && messageStart.TryGetProperty("usage", out var usageStart))
                        {
                            if (usageStart.TryGetProperty("input_tokens", out var inTok) && inTok.ValueKind == JsonValueKind.Number)
                                inputTokens = inTok.GetInt32();
                            _logger?.LogDebug("Parsed input tokens from message_start: {InputTokens}", inputTokens);
                        }
                        break;

                    case "content_block_start":
                        if (root.TryGetProperty("content_block", out var blockStart) && blockStart.TryGetProperty("type", out var blockType) && blockType.GetString() == "tool_use")
                        {
                            if (blockStart.TryGetProperty("id", out var toolId) && blockStart.TryGetProperty("name", out var toolName) &&
                                root.TryGetProperty("index", out var startIndexElement) && startIndexElement.TryGetInt32(out int startIndex))
                            {
                                toolCallInfo = new ToolCallChunk(startIndex, Id: toolId.GetString(), Name: toolName.GetString(), ArgumentChunk: null);
                                _logger?.LogInformation("Parsed tool_use start: Index={Index}, Id={Id}, Name={Name}", startIndex, toolCallInfo.Id, toolCallInfo.Name);
                            }
                            else
                            {
                                _logger?.LogWarning("Could not parse tool_use start details from: {Json}", rawJson);
                            }
                        }
                        break;

                    case "content_block_delta":
                        if (root.TryGetProperty("index", out var deltaIndexElement) && deltaIndexElement.TryGetInt32(out int deltaIndex))
                        {
                            if (root.TryGetProperty("delta", out var contentDelta) && contentDelta.TryGetProperty("type", out var deltaTypeElement))
                            {
                                string deltaType = deltaTypeElement.GetString() ?? string.Empty;

                                if (deltaType == "text_delta" && contentDelta.TryGetProperty("text", out var textElement))
                                {
                                    textDelta = textElement.GetString();
                                    _logger?.LogDebug("Parsed text_delta: Index={Index}, Text='{TextDelta}'", deltaIndex, textDelta);
                                }
                                else if (deltaType == "input_json_delta" && contentDelta.TryGetProperty("partial_json", out var argsChunkElement))
                                {
                                    toolCallInfo = new ToolCallChunk(deltaIndex, ArgumentChunk: argsChunkElement.GetString());
                                    _logger?.LogDebug("Parsed tool argument chunk (input_json_delta): Index={Index}, Length={Length}", deltaIndex, toolCallInfo.ArgumentChunk?.Length ?? 0);
                                }
                                else if (deltaType == "thinking_delta" && contentDelta.TryGetProperty("thinking", out var thinkingElement))
                                {
                                    thinkingDelta = thinkingElement.GetString();
                                    _logger?.LogDebug("Parsed thinking_delta: Index={Index}, Text='{ThinkingDelta}'", deltaIndex, thinkingDelta);
                                }
                            }
                        }
                        else
                        {
                            _logger?.LogWarning("Could not parse content_block_delta details (missing index or delta info) from: {Json}", rawJson);
                        }
                        break;

                    case "content_block_stop":
                        if (root.TryGetProperty("index", out var stopIndexElement) && stopIndexElement.TryGetInt32(out int stopIndex))
                        {
                            _logger?.LogDebug("Received content_block_stop for index {Index}", stopIndex);
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("delta", out var messageDelta) && messageDelta.TryGetProperty("stop_reason", out var reasonElement))
                        {
                            var reason = reasonElement.GetString();
                            finishReason = reason switch
                            {
                                "end_turn" => "stop",
                                "max_tokens" => "length",
                                "tool_calls" => "tool_calls", 
                                _ => reason
                            };
                            _logger?.LogInformation("Received stop reason: {Reason} (normalized to: {NormalizedReason})", reason, finishReason);
                        }
                        if (root.TryGetProperty("usage", out var usageDelta))
                        {
                            if (usageDelta.TryGetProperty("output_tokens", out var outTok) && outTok.ValueKind == JsonValueKind.Number)
                                outputTokens = outTok.GetInt32();
                            _logger?.LogDebug("Parsed output tokens from message_delta usage: {OutputTokens}", outputTokens);
                        }
                        break;

                    case "message_stop":
                        _logger?.LogInformation("Received message_stop event.");
                        if (string.IsNullOrEmpty(finishReason))
                        {
                            finishReason = "stop";
                            _logger?.LogInformation("Assuming normal 'stop' finish reason for message_stop.");
                        }
                         if (root.TryGetProperty("message", out var finalMessage) && finalMessage.TryGetProperty("usage", out var finalUsage))
                         {
                             if (finalUsage.TryGetProperty("input_tokens", out var finalInTok) && finalInTok.ValueKind == JsonValueKind.Number) inputTokens = finalInTok.GetInt32();
                             if (finalUsage.TryGetProperty("output_tokens", out var finalOutTok) && finalOutTok.ValueKind == JsonValueKind.Number) outputTokens = finalOutTok.GetInt32();
                             if (root.TryGetProperty("message", out var startMsg) && startMsg.TryGetProperty("usage", out var startUsage) &&
                                 startUsage.TryGetProperty("input_tokens", out var startInTok) && startInTok.ValueKind == JsonValueKind.Number && !inputTokens.HasValue)
                             {
                                 inputTokens = startInTok.GetInt32();
                             }
                             if (inputTokens.HasValue || outputTokens.HasValue)
                             {
                                 _logger?.LogDebug("Parsed final token usage from message_stop: Input={Input}, Output={Output}", inputTokens, outputTokens);
                             }
                         }
                        break;

                    case "ping":
                        _logger?.LogTrace("Received ping event");
                        break;

                    default:
                        _logger?.LogWarning("Unknown event type: {EventType}", eventType);
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
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse Anthropic stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing Anthropic stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo();
        }
    }
} 