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

        Logger?.LogInformation("[BflApiDebug] Raw chunk content: {RawContent}", jsonDoc.RootElement.GetRawText());

        if (root.TryGetProperty("error", out var errorElement))
        {
            Logger?.LogError("BFL API stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error");
        }

        // Handle markdown image response
        if (root.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            string text = textElement.GetString()!;
            if (text.StartsWith("![generated image]"))
            {
                textDelta = text;
                finishReason = "stop";
                Logger?.LogInformation("Parsed BFL API image markdown: '{TextDelta}'", textDelta);
                return new ParsedChunkInfo(textDelta, finishReason);
            }
        }

        // Handle polling response format
        if (root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String)
        {
            var status = statusElement.GetString()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(status))
            {
                switch (status)
                {
                    case "error":
                    case "failed":
                        finishReason = "error";
                        break;
                    case "ready":
                    case "completed":
                    case "done": 
                        finishReason = "stop";
                        break;
                    case "interrupted":
                        finishReason = "interrupted"; 
                        break;
                    case "processing":
                    case "queued":
                        textDelta = "Image generation in progress...";
                        break;
                    default:
                        Logger?.LogWarning("Unknown BFL API status: {Status}", status);
                        break;
                }
                Logger?.LogInformation("Parsed BFL API status: {Status}, mapped to FinishReason: {FinishReason}", status, finishReason);
            }
        }

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            FinishReason: finishReason
        );
    }
} 