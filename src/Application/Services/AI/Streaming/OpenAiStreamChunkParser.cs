using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class OpenAiStreamChunkParser : BaseStreamChunkParser<OpenAiStreamChunkParser>
{
    public OpenAiStreamChunkParser(ILogger<OpenAiStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.OpenAi;

    protected override ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        // The main try-catch block is now in the base class.
        // The null/empty check for rawJson is also in the base class.
        Logger?.LogInformation("[OpenAiParser] Received raw data chunk: {RawContent}", rawJson);

        using var doc = JsonDocument.Parse(rawJson); // This can throw JsonException, handled by base.
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            Logger?.LogWarning("OpenAI stream chunk missing or invalid 'type' property: {RawJson}", rawJson);
            return new ParsedChunkInfo(); // Or perhaps a specific error finish reason
        }

        var eventType = typeElement.GetString();
        string? textDelta = null;
        string? thinkingDelta = null;
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
                    Logger?.LogTrace("Parsed OpenAI text delta: '{TextDelta}'", textDelta);
                }
                break;

            case "response.function_call.invoked":
                // ignore legacy invocation events
                break;

            case "response.completed":
                Logger?.LogInformation("Received OpenAI completion event.");
                if (root.TryGetProperty("response", out var responseElement))
                {
                    if (responseElement.TryGetProperty("usage", out var usage))
                    {
                        if (usage.TryGetProperty("input_tokens", out var promptTokens)) inputTokens = promptTokens.GetInt32();
                        if (usage.TryGetProperty("output_tokens", out var completionTokens)) outputTokens = completionTokens.GetInt32();
                        Logger?.LogDebug("Parsed OpenAI token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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
                                    Logger?.LogError("OpenAI completion event reported error: {ErrorJson}", errorDetails);
                                }
                                break;
                            case "incomplete": finishReason = "length"; Logger?.LogWarning("OpenAI completion event reported incomplete status."); break;
                            default: finishReason = status; break;
                        }
                        Logger?.LogInformation("Parsed OpenAI finish status: {Status}, mapped to reason: {FinishReason}", status, finishReason);
                    }
                }
                break;

            case "response.error":
                Logger?.LogError("Received OpenAI error event: {ErrorJson}", root.GetRawText());
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
                    Logger?.LogInformation("Function call started: {Name} (id={Id})", name, id);
                }
                break;

            case "response.function_call_arguments.delta":
                if (root.TryGetProperty("delta", out var argDelta) && argDelta.ValueKind == JsonValueKind.String)
                {
                    var chunk = argDelta.GetString() ?? string.Empty;
                    var idx2 = root.GetProperty("output_index").GetInt32();
                    toolCallInfo = new ToolCallChunk(idx2, ArgumentChunk: chunk);
                    Logger?.LogTrace("[OpenAiParser] Function call arg chunk: {Chunk}", chunk);
                }
                break;

            case "response.function_call_arguments.done":
                // signal arguments complete; argument chunks already accumulated
                {
                    var idx3 = root.GetProperty("output_index").GetInt32();
                    toolCallInfo = new ToolCallChunk(idx3, ArgumentChunk: null, IsComplete: true);
                    Logger?.LogInformation("[OpenAiParser] Function call arguments complete for index {Index}", idx3);
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
                    Logger?.LogInformation("[OpenAiParser] Function call completed: {Name} (id={Id})", name4, id4);
                }
                break;

            case "response.created":
            case "response.in_progress":
                Logger?.LogTrace("[OpenAiParser] Received meta event: {EventType}", eventType);
                break;
            
            case "response.reasoning_summary_text.delta":
                if (root.TryGetProperty("delta", out var reasoningDelta) && reasoningDelta.ValueKind == JsonValueKind.String)
                {
                    thinkingDelta = reasoningDelta.GetString();
                    Logger?.LogTrace("Parsed OpenAI reasoning delta: '{ThinkingDelta}'", thinkingDelta);
                }
                break;

            default:
                Logger?.LogWarning("Unhandled OpenAI event type: {EventType}", eventType);
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
        // The outer try-catch for general exceptions and JsonException is in the base class.
    }
}