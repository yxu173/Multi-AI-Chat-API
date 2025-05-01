using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Application.Services.Streaming;

public class GrokStreamChunkParser : IStreamChunkParser
{
    private readonly ILogger<GrokStreamChunkParser> _logger;

    public GrokStreamChunkParser(ILogger<GrokStreamChunkParser> logger)
    {
        _logger = logger;
    }

    public ModelType SupportedModelType => ModelType.Grok;

    public ParsedChunkInfo ParseChunk(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "[DONE]")
        {
            _logger?.LogTrace("[GrokParser] Received empty or DONE marker, indicating end of stream (handled upstream).");
            return new ParsedChunkInfo(); 
        }

        try
        {
            _logger?.LogTrace("[GrokParser] Received raw data chunk: {RawContent}", rawJson);

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? textDelta = null;
            string? thinkingDelta = null;
            int? inputTokens = null;
            int? outputTokens = null;
            string? finishReason = null; 
            ToolCallChunk? toolCallInfo = null; 

            if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array && choicesElement.GetArrayLength() > 0)
            {
                var firstChoice = choicesElement[0];
                
                if (firstChoice.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.Object)
                {
                    if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    {
                        textDelta = contentElement.GetString();
                        _logger?.LogTrace("Parsed Grok text delta: '{TextDelta}'", textDelta);
                    }
                    
                    // Parse function call information
                    if (deltaElement.TryGetProperty("tool_calls", out var toolCallsElement) && 
                        toolCallsElement.ValueKind == JsonValueKind.Array &&
                        toolCallsElement.GetArrayLength() > 0)
                    {
                        var toolCall = toolCallsElement[0];
                        
                        string? functionId = null;
                        string? functionName = null;
                        string? argumentChunk = null;
                        
                        // Extract function call id if available
                        if (toolCall.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                        {
                            functionId = idElement.GetString();
                        }
                        
                        // Extract function information
                        if (toolCall.TryGetProperty("function", out var functionElement) && functionElement.ValueKind == JsonValueKind.Object)
                        {
                            // Get function name
                            if (functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                            {
                                functionName = nameElement.GetString();
                            }
                            
                            // Get function arguments
                            if (functionElement.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.String)
                            {
                                argumentChunk = argsElement.GetString();
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(functionId) || !string.IsNullOrEmpty(functionName) || !string.IsNullOrEmpty(argumentChunk))
                        {
                            toolCallInfo = new ToolCallChunk(
                                Index: 0,
                                Id: functionId,
                                Name: functionName,
                                ArgumentChunk: argumentChunk,
                                IsComplete: false
                            );
                            
                            _logger?.LogTrace("Parsed Grok function call: Id={Id}, Name={Name}, Args={Args}", 
                                functionId, functionName, argumentChunk);
                        }
                    }
                    
                    if (firstChoice.TryGetProperty("finish_reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
                    { 
                        finishReason = reasonElement.GetString();
                        _logger?.LogDebug("Parsed Grok finish reason from choice: {FinishReason}", finishReason);
                    }
                }
                
                if (deltaElement.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String)
                {
                    thinkingDelta = reasoningElement.GetString();
                    _logger?.LogTrace("Parsed Grok reasoning content: '{ReasoningContent}'", thinkingDelta);
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
                    _logger?.LogTrace("Found reasoning tokens: {ReasoningTokens}", rt);
                    outputTokens = (outputTokens ?? 0) + rt; 
                }
                
                _logger?.LogTrace("Parsed Grok token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
            }

            if (finishReason == null && textDelta == null && thinkingDelta == null && toolCallInfo == null)
            {
                _logger?.LogTrace("Grok chunk has no text delta, reasoning, tool calls, or explicit finish reason. Might be final data chunk before [DONE].");
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
            _logger?.LogError(jsonEx, "Failed to parse Grok stream chunk JSON. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error"); 
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error parsing Grok stream chunk. RawChunk: {RawChunk}", rawJson);
            return new ParsedChunkInfo(FinishReason: "error");
        }
    }
} 