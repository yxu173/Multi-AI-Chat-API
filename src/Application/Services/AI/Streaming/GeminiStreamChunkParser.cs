using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class GeminiStreamChunkParser : BaseStreamChunkParser<GeminiStreamChunkParser>
{
    public GeminiStreamChunkParser(ILogger<GeminiStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.Gemini;

    protected override ParsedChunkInfo ParseModelSpecificChunkWithReader(ref Utf8JsonReader reader)
    {
        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Read();
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "candidates":
                    ParseCandidatesArray(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo, ref finishReason);
                    break;
                case "usageMetadata":
                    ParseUsageMetadata(ref reader, ref inputTokens, ref outputTokens);
                    break;
                case "promptFeedback":
                    // Handle prompt feedback if needed
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
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

    private void ParseCandidatesArray(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo, ref string? finishReason)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            ParseCandidateObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo, ref finishReason);
        }
    }

    private void ParseCandidateObject(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo, ref string? finishReason)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "content":
                    ParseContentObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
                    break;
                case "finishReason":
                    var reason = reader.GetString();
                    finishReason = reason switch
                    {
                        "STOP" => "stop",
                        "MAX_TOKENS" => "length",
                        "SAFETY" => "stop",
                        "RECITATION" => "stop",
                        _ => reason?.ToLowerInvariant()
                    };
                    break;
                case "index":
                case "safetyRatings":
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private void ParseContentObject(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "parts":
                    ParsePartsArray(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
                    break;
                case "role":
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private void ParsePartsArray(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            ParsePartObject(ref reader, ref textDelta, ref thinkingDelta, ref toolCallInfo);
        }
    }

    private void ParsePartObject(ref Utf8JsonReader reader, ref string? textDelta, ref string? thinkingDelta, ref ToolCallChunk? toolCallInfo)
    {
        string? partType = null;
        string? partText = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "text":
                    partText = reader.GetString();
                    break;
                case "functionCall":
                    ParseFunctionCallObject(ref reader, ref toolCallInfo);
                    break;
                case "inlineData":
                case "fileData":
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (!string.IsNullOrEmpty(partText))
        {
            textDelta = partText;
        }
    }

    private void ParseFunctionCallObject(ref Utf8JsonReader reader, ref ToolCallChunk? toolCallInfo)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        string? functionName = null;
        string? functionArgs = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "name":
                    functionName = reader.GetString();
                    break;
                case "args":
                    functionArgs = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (!string.IsNullOrEmpty(functionName))
        {
            toolCallInfo = new ToolCallChunk(0, null, functionName, functionArgs);
        }
    }

    private void ParseUsageMetadata(ref Utf8JsonReader reader, ref int? inputTokens, ref int? outputTokens)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "promptTokenCount":
                    inputTokens = reader.GetInt32();
                    break;
                case "candidatesTokenCount":
                    outputTokens = reader.GetInt32();
                    break;
                case "totalTokenCount":
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        // Parse candidates array
        if (root.TryGetProperty("candidates", out var candidatesElement) && candidatesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidatesElement.EnumerateArray())
            {
                if (candidate.TryGetProperty("content", out var contentElement))
                {
                    if (contentElement.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in partsElement.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                            {
                                textDelta = textElement.GetString();
                                Logger?.LogTrace("Parsed Gemini text delta: '{TextDelta}'", textDelta);
                            }
                            else if (part.TryGetProperty("functionCall", out var functionCallElement))
                            {
                                if (functionCallElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                                {
                                    var functionName = nameElement.GetString();
                                    string? functionArgs = null;
                                    if (functionCallElement.TryGetProperty("args", out var argsElement))
                                    {
                                        functionArgs = argsElement.GetRawText();
                                    }
                                    toolCallInfo = new ToolCallChunk(0, null, functionName, functionArgs);
                                    Logger?.LogTrace("Parsed Gemini function call: {FunctionName}", functionName);
                                }
                            }
                        }
                    }
                }

                if (candidate.TryGetProperty("finishReason", out var finishReasonElement) && finishReasonElement.ValueKind == JsonValueKind.String)
                {
                    var reason = finishReasonElement.GetString();
                    finishReason = reason switch
                    {
                        "STOP" => "stop",
                        "MAX_TOKENS" => "length",
                        "SAFETY" => "stop",
                        "RECITATION" => "stop",
                        _ => reason?.ToLowerInvariant()
                    };
                    Logger?.LogDebug("Parsed Gemini finish reason: {FinishReason}", finishReason);
                }
            }
        }

        // Parse usage metadata
        if (root.TryGetProperty("usageMetadata", out var usageElement))
        {
            if (usageElement.TryGetProperty("promptTokenCount", out var promptTokens)) inputTokens = promptTokens.GetInt32();
            if (usageElement.TryGetProperty("candidatesTokenCount", out var completionTokens)) outputTokens = completionTokens.GetInt32();
            Logger?.LogDebug("Parsed Gemini token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
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