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
            _logger?.LogInformation("[OpenAiParser] Received raw data chunk: {RawContent}", rawJson);

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
                    // ignore legacy invocation events
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
                                case "completed": finishReason = "stop"; break;
                                case "error":
                                case "failed":
                                    finishReason = "error";
                                    if (responseElement.TryGetProperty("error", out var errorObj))
                                    {
                                        errorDetails = errorObj.ToString();
                                        _logger?.LogError("OpenAI completion event reported error: {ErrorJson}", errorDetails);
                                    }
                                    break;
                                case "incomplete": finishReason = "length"; _logger?.LogWarning("OpenAI completion event reported incomplete status."); break;
                                default: finishReason = status; break;
                            }
                            _logger?.LogInformation("Parsed OpenAI finish status: {Status}, mapped to reason: {FinishReason}", status, finishReason);
                        }
                    }
                    break;

                case "response.error":
                    _logger?.LogError("Received OpenAI error event: {ErrorJson}", root.GetRawText());
                    finishReason = "error";
                    if (root.TryGetProperty("error", out var errorContent)) errorDetails = errorContent.ToString();
                    break;

                case "response.output_item.added":
                    // start of function call
                    if (root.TryGetProperty("item", out var addedItem)
                     && addedItem.GetProperty("type").GetString() == "function_call")
                    {
                        int idx = root.GetProperty("output_index").GetInt32();
                        string? id = addedItem.GetProperty("call_id").GetString();
                        string? name = addedItem.GetProperty("name").GetString();
                        toolCallInfo = new ToolCallChunk(idx, id, name);
                        _logger?.LogInformation("Function call started: {Name} (id={Id})", name, id);
                    }
                    break;

                case "response.function_call_arguments.delta":
                    // accumulate function call argument chunks
                    if (root.TryGetProperty("delta", out var argDelta) && argDelta.ValueKind == JsonValueKind.String)
                    {
                        var chunk = argDelta.GetString() ?? string.Empty;
                        var idx2 = root.GetProperty("output_index").GetInt32();
                        toolCallInfo = new ToolCallChunk(idx2, ArgumentChunk: chunk);
                        _logger?.LogTrace("[OpenAiParser] Function call arg chunk: {Chunk}", chunk);
                    }
                    break;

                case "response.function_call_arguments.done":
                    // signal arguments complete; argument chunks already accumulated
                    {
                        var idx3 = root.GetProperty("output_index").GetInt32();
                        toolCallInfo = new ToolCallChunk(idx3, ArgumentChunk: null, IsComplete: true);
                        _logger?.LogInformation("[OpenAiParser] Function call arguments complete for index {Index}", idx3);
                    }
                    break;

                case "response.output_item.done":
                    if (root.TryGetProperty("item", out var doneItem)
                     && doneItem.GetProperty("type").GetString() == "function_call")
                    {
                        var idx4 = root.GetProperty("output_index").GetInt32();
                        var id4 = doneItem.GetProperty("call_id").GetString();
                        var name4 = doneItem.GetProperty("name").GetString();
                        // finalize function call; use full args from previous event
                        toolCallInfo = new ToolCallChunk(idx4, id4, name4, ArgumentChunk: null, IsComplete: true);
                        finishReason = "function_call";
                        _logger?.LogInformation("[OpenAiParser] Function call completed: {Name} (id={Id})", name4, id4);
                    }
                    break;

                case "response.created":
                case "response.in_progress":
                    _logger?.LogTrace("[OpenAiParser] Received meta event: {EventType}", eventType);
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