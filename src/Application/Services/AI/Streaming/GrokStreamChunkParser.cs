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

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        ToolCallChunk? toolCallInfo = null;

        if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array && choicesElement.GetArrayLength() > 0)
        {
            var firstChoice = choicesElement[0];

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
                    var toolCallDelta = toolCallsElement[0];
                    string? functionId = null;
                    string? functionName = null;
                    string? argumentChunk = null;
                    int toolCallIndex = 0; 

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
                if (deltaElement.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                { 
                    thinkingDelta = reasoningElement.GetString();
                    Logger?.LogTrace("Parsed Grok reasoning content (thinking delta): '{ReasoningContent}'", thinkingDelta);
                }
            }
        }

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
            if (usageElement.TryGetProperty("reasoning_tokens", out var reasoningTokensElement) && reasoningTokensElement.TryGetInt32(out var rt))
            {
                Logger?.LogTrace("Found Grok reasoning tokens in usage: {ReasoningTokens}", rt);
                outputTokens = (outputTokens ?? 0) + rt;
            }
            Logger?.LogTrace("Parsed Grok token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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