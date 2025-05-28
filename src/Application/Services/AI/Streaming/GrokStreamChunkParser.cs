using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class GrokStreamChunkParser : BaseStreamChunkParser<GrokStreamChunkParser>
{
    public GrokStreamChunkParser(ILogger<GrokStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.Grok;

    protected override ParsedChunkInfo ParseModelSpecificChunk(string rawJson)
    {
        // The base class handles null/empty and the "[DONE]" marker for Grok is typically handled by the caller or StreamProcessor
        // by virtue of it not being valid JSON. If Grok sends "[DONE]" as a separate line not part of JSON stream, 
        // this parser won't see it. If it's embedded in a way that breaks JSON, base class handles JsonException.
        // The original check `rawJson == "[DONE]"` implies it might come as a standalone string.
        // If StreamProcessor pre-filters "[DONE]", this is fine. Otherwise, if it's passed here and isn't JSON,
        // base class JsonException handler will catch it.

        Logger?.LogTrace("[GrokParser] Received raw data chunk: {RawContent}", rawJson);

        using var doc = JsonDocument.Parse(rawJson); // Handled by base if this fails
        var root = doc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        ToolCallChunk? toolCallInfo = null;

        // Grok's structure is similar to OpenAI: choices -> delta -> content/tool_calls
        if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array && choicesElement.GetArrayLength() > 0)
        {
            var firstChoice = choicesElement[0];

            // Finish reason can be on the choice itself
            if (firstChoice.TryGetProperty("finish_reason", out var choiceFinishReasonElement) && choiceFinishReasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = choiceFinishReasonElement.GetString();
                Logger?.LogDebug("Parsed Grok finish reason from choice: {FinishReason}", finishReason);
            }

            if (firstChoice.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.Object)
            {
                if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                {
                    textDelta = contentElement.GetString();
                    Logger?.LogTrace("Parsed Grok text delta: '{TextDelta}'", textDelta);
                }

                // Parse tool call information from delta
                if (deltaElement.TryGetProperty("tool_calls", out var toolCallsElement) &&
                    toolCallsElement.ValueKind == JsonValueKind.Array &&
                    toolCallsElement.GetArrayLength() > 0)
                {
                    var toolCallDelta = toolCallsElement[0]; // Assuming one tool call part per delta chunk
                    string? functionId = null;
                    string? functionName = null;
                    string? argumentChunk = null;
                    int toolCallIndex = 0; // Grok tool_calls in delta might have an index property

                    if (toolCallDelta.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out int idx))
                    {
                        toolCallIndex = idx;
                    }

                    if (toolCallDelta.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                    {
                        functionId = idElement.GetString();
                    }

                    if (toolCallDelta.TryGetProperty("function", out var functionElement) && functionElement.ValueKind == JsonValueKind.Object)
                    {
                        if (functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                        {
                            functionName = nameElement.GetString();
                        }
                        if (functionElement.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.String)
                        {
                            argumentChunk = argsElement.GetString();
                        }
                    }

                    if (!string.IsNullOrEmpty(functionId) || !string.IsNullOrEmpty(functionName) || !string.IsNullOrEmpty(argumentChunk))
                    {
                        // IsComplete for a tool call chunk is tricky. If finish_reason is "tool_calls" on the main choice,
                        // it implies the model has finished generating all tool call parts.
                        // Individual argument chunks are streamed. We mark IsComplete=true if this chunk represents a fully formed call object, 
                        // which is often the case when finish_reason = "tool_calls".
                        // For Grok, similar to OpenAI, arguments are streamed. The final part of a tool_call would have the full argument string.
                        // Let StreamProcessor decide on overall completion of a tool call; here we mark IsComplete if the arguments seem complete for *this* chunk's view.
                        // If `finishReason` on the choice is `tool_calls`, then this is likely a complete tool call definition.
                        bool isCurrentToolCallComplete = (finishReason == "tool_calls" && !string.IsNullOrEmpty(functionName) && !string.IsNullOrEmpty(argumentChunk)); 
                        
                        toolCallInfo = new ToolCallChunk(
                            Index: toolCallIndex,
                            Id: functionId,
                            Name: functionName,
                            ArgumentChunk: argumentChunk,
                            IsComplete: isCurrentToolCallComplete 
                        );
                        Logger?.LogTrace("Parsed Grok tool call delta: Index={ToolIndex}, Id={Id}, Name={Name}, Args={HasArgs}, CompleteHere={IsComplete}",
                            toolCallIndex, functionId, functionName, !string.IsNullOrEmpty(argumentChunk), isCurrentToolCallComplete);
                    }
                }
                 // Grok specific: reasoning_content in delta
                if (deltaElement.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                { // This seems to be Grok's version of a thinking/reasoning step output
                    thinkingDelta = reasoningElement.GetString();
                    Logger?.LogTrace("Parsed Grok reasoning content (thinking delta): '{ReasoningContent}'", thinkingDelta);
                }
            }
        }

        // Usage can also be a top-level property in some Grok responses (especially non-streaming or final message)
        // For streaming, it might appear with the final chunk associated with a finish_reason.
        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement) && promptTokensElement.TryGetInt32(out var pt))
            {
                inputTokens = pt;
            }
            if (usageElement.TryGetProperty("completion_tokens", out var completionTokensElement) && completionTokensElement.TryGetInt32(out var ct))
            {
                outputTokens = ct;
            }
            // Grok also has "reasoning_tokens" in usage.
            if (usageElement.TryGetProperty("reasoning_tokens", out var reasoningTokensElement) && reasoningTokensElement.TryGetInt32(out var rt))
            {
                Logger?.LogTrace("Found Grok reasoning tokens in usage: {ReasoningTokens}", rt);
                // Add to output tokens as they are part of the model's generation.
                outputTokens = (outputTokens ?? 0) + rt;
            }
            Logger?.LogTrace("Parsed Grok token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
        }
        
        // If x_groq and its usage field are present (another way Grok reports final usage)
        if (root.TryGetProperty("x_groq", out var xGroqElement) && xGroqElement.TryGetProperty("usage", out var xGroqUsageElement))
        {
            if (xGroqUsageElement.TryGetProperty("prompt_tokens", out var ptElement) && ptElement.TryGetInt32(out var pt))
            {
                inputTokens = pt; // Overwrite if more specific final count is available
            }
            if (xGroqUsageElement.TryGetProperty("completion_tokens", out var ctElement) && ctElement.TryGetInt32(out var ct))
            {
                outputTokens = ct; // Overwrite
            }
             if (xGroqUsageElement.TryGetProperty("reasoning_tokens", out var rtElement) && rtElement.TryGetInt32(out var rt))
            {
                // outputTokens may have already included reasoning_tokens from delta's usage, ensure not to double count
                // This path is more for a final summary.
                Logger?.LogTrace("Found Grok reasoning tokens in x_groq usage: {ReasoningTokens}", rt);
                // It's safer to rely on a comprehensive completion_tokens if available, or sum if not.
            }
            if (xGroqUsageElement.TryGetProperty("total_tokens", out var ttElement) && ttElement.TryGetInt32(out var tt))
            {
                 // If we have prompt and total, we can be more sure about completion tokens.
            }
            Logger?.LogDebug("Parsed Grok x_groq token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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