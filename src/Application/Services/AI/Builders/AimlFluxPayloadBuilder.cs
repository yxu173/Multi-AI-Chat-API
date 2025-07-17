using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Application.Services.AI.Interfaces;

namespace Application.Services.AI.Builders;

public class AimlFluxPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    public AimlFluxPayloadBuilder(ILogger<AimlFluxPayloadBuilder> logger)
        : base(logger)
    {
    }

    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<PluginDefinition>? tools = null, CancellationToken cancellationToken = default)
    {
        Logger?.LogInformation("Preparing payload for AIMLAPI Flux model {ModelCode}", context.SpecificModel.ModelCode);

        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;

        var latestUserMessageText = context.History
            .Where(m => !m.IsFromAi && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => ExtractTextFromMessage(m.Content))
            .LastOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (string.IsNullOrWhiteSpace(latestUserMessageText))
        {
            Logger?.LogError("Cannot prepare AIMLAPI request: No valid user prompt text found in history for ChatSession {ChatSessionId}", context.ChatSession.Id);
            throw new InvalidOperationException("Cannot generate image without a valid user prompt text.");
        }
        requestObj["prompt"] = latestUserMessageText.Trim();

       // AddParameters(requestObj, context);
        
        requestObj["image_size"] = context.ImageSize ?? "landscape_16_9";
        requestObj["num_images"] = context.NumImages ?? 1;
        requestObj["output_format"] = context.OutputFormat ?? "jpeg";

        Logger?.LogDebug("AIMLAPI Payload Prepared: Model={Model}, Prompt='{Prompt}', Size={ImageSize}, Num={NumImages}, Format={Format}, Safety={Safety}, Tolerance={Tolerance}",
            requestObj.GetValueOrDefault("model", "N/A"),
            requestObj.GetValueOrDefault("prompt", "N/A"),
            requestObj.GetValueOrDefault("image_size", "N/A"),
            requestObj.GetValueOrDefault("num_images", "N/A"),
            requestObj.GetValueOrDefault("output_format", "N/A"),
            requestObj.GetValueOrDefault("enable_safety_checker", "N/A"),
            requestObj.GetValueOrDefault("safety_tolerance", "N/A"));

        return Task.FromResult(new AiRequestPayload(requestObj)); 
    }
} 