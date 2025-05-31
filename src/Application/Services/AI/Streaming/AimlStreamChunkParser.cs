using System.Text.Json;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public class AimlStreamChunkParser : BaseStreamChunkParser<AimlStreamChunkParser>
{
    public AimlStreamChunkParser(ILogger<AimlStreamChunkParser> logger)
        : base(logger)
    {
    }

    public override ModelType SupportedModelType => ModelType.AimlFlux;

    protected override ParsedChunkInfo ParseModelSpecificChunkInternal(JsonDocument jsonDoc)
    {
        var root = jsonDoc.RootElement;

        string? textDelta = null;
        string? finishReason = null;

        Logger?.LogInformation("[AimlDebug] Raw chunk content: {RawContent}", jsonDoc.RootElement.GetRawText());

        if (root.TryGetProperty("error", out var errorElement))
        {
            Logger?.LogError("AIML stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error");
        }

        if (root.TryGetProperty("rawContent", out var rawContentElement) && rawContentElement.ValueKind == JsonValueKind.String)
        {
            textDelta = rawContentElement.GetString();
            Logger?.LogInformation("Parsed AIML text content: '{TextDelta}'", textDelta);
        }
        else if (root.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.String)
        {
            textDelta = deltaElement.GetString();
            Logger?.LogInformation("Parsed AIML delta content: '{TextDelta}'", textDelta);
        }
        else if (root.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            textDelta = textElement.GetString();
            Logger?.LogInformation("Parsed AIML text content (from 'text' field): '{TextDelta}'", textDelta);
        }

        if (root.TryGetProperty("isCompletion", out var completionElement) && completionElement.ValueKind == JsonValueKind.True)
        {
            finishReason = "stop"; 
            Logger?.LogInformation("AIML stream chunk indicates completion via isCompletion=true.");
        }

        if (root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String)
        {
            var status = statusElement.GetString()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(status))
            {
                switch (status)
                {
                    case "error":
                        finishReason = "error";
                        break;
                    case "completed":
                    case "done": 
                        finishReason = "stop";
                        break;
                    case "interrupted":
                        finishReason = "interrupted"; 
                        break;
                    default:
                        Logger?.LogWarning("Unknown AIML status: {Status}", status);
                        break;
                }
                Logger?.LogInformation("Parsed AIML status: {Status}, mapped to FinishReason: {FinishReason}", status, finishReason);
            }
        }
        
        if (root.TryGetProperty("done", out var doneFlag) && doneFlag.ValueKind == JsonValueKind.True && string.IsNullOrEmpty(finishReason))
        {
            finishReason = "stop";
             Logger?.LogInformation("AIML stream chunk indicates completion via done=true.");
        }

        

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            FinishReason: finishReason
        );
    }
} 