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
            _logger?.LogTrace("[OpenAiParser] Received raw data chunk: {RawContent}", rawJson);

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                _logger?.LogWarning("OpenAI stream chunk missing or invalid 'type' property: {RawJson}", rawJson);
                return new ParsedChunkInfo(); 
            }

            var eventType = typeElement.GetString();
            string? textDelta = null;
            ToolCallChunk? toolCallInfo = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null;
            string? errorDetails = null;

            switch (eventType)
            {
                case "response.output_text.delta":
                    if (root.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = deltaElement.GetString();
                        _logger?.LogTrace("Parsed OpenAI text delta: '{TextDelta}'", textDelta);
                    }
                    break;

                case "response.function_call.invoked":
                    string? toolCallId = null;
                    string? toolCallName = null;
                    string? argumentChunk = null;
                    int toolCallIndex = 0; 

                    if (root.TryGetProperty("call", out var callElement))
                    {
                        if (callElement.TryGetProperty("call_id", out var idElement)) toolCallId = idElement.GetString();
                        if (callElement.TryGetProperty("name", out var nameElement)) toolCallName = nameElement.GetString();
                        if (callElement.TryGetProperty("arguments", out var argsElement)) argumentChunk = argsElement.GetString();
                    }

                    toolCallInfo = new ToolCallChunk(toolCallIndex, toolCallId, toolCallName, ArgumentChunk: argumentChunk);
                    _logger?.LogInformation("Parsed OpenAI function call invocation. Id: {Id}, Name: {Name}, Args Length: {Length}", toolCallId, toolCallName, argumentChunk?.Length ?? 0);
                    break;

                case "response.completed":
                    _logger?.LogInformation("Received OpenAI completion event.");
                    if (root.TryGetProperty("response", out var responseElement))
                    {
                        if (responseElement.TryGetProperty("usage", out var usage))
                        {
                            if (usage.TryGetProperty("input_tokens", out var promptTokens)) inputTokens = promptTokens.GetInt32();
                            if (usage.TryGetProperty("output_tokens", out var completionTokens)) outputTokens = completionTokens.GetInt32();
                            _logger?.LogDebug("Parsed OpenAI token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
                        }

                        if (responseElement.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String)
                        {
                            var status = statusElement.GetString();
                            switch (status)
                            {
                                case "completed":
                                    finishReason = "stop"; 
                                    break;
                                case "error":
                                case "failed": 
                                    finishReason = "error";
                                    if (responseElement.TryGetProperty("error", out var errorObj))
                                    {
                                        errorDetails = errorObj.ToString(); 
                                        _logger?.LogError("OpenAI completion event reported error: {ErrorJson}", errorDetails);
                                    }
                                    break;
                                case "incomplete":
                                      finishReason = "length"; 
                                     _logger?.LogWarning("OpenAI completion event reported incomplete status.");
                                     break;
                                default:
                                    finishReason = status; 
                                    break;
                            }
                            _logger?.LogInformation("Parsed OpenAI finish status: {Status}, mapped to reason: {FinishReason}", status, finishReason);
                        }
                    }
                    break;

                case "response.error":
                    _logger?.LogError("Received OpenAI error event: {ErrorJson}", root.GetRawText());
                    finishReason = "error";
                    if (root.TryGetProperty("error", out var errorContent)) {
                         errorDetails = errorContent.ToString();
                    }
                    break;

                case "response.created":
                case "response.in_progress":
                case "response.output_item.added":
                case "response.content_part.added":
                case "response.output_text.done":
                case "response.content_part.done":
                case "response.output_item.done":
                    _logger?.LogTrace("Received OpenAI meta/structure event: {EventType}", eventType);
                    break;

                default:
                    _logger?.LogWarning("Unhandled OpenAI event type: {EventType}", eventType);
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
        catch (JsonException jsonEx)
        {
            _logger?.LogError(jsonEx, "Failed to parse OpenAI stream chunk JSON. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error parsing OpenAI stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error");
        }
    }
}