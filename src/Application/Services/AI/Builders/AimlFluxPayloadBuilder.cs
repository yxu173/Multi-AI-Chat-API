using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.Interfaces;

namespace Application.Services.AI.Builders;

public class AimlFluxPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    public AimlFluxPayloadBuilder(ILogger<AimlFluxPayloadBuilder> logger)
        : base(logger)
    {
    }

    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<object>? tools = null, CancellationToken cancellationToken = default)
    {
        Logger?.LogInformation("Preparing payload for BFL API Flux Pro model {ModelCode}", context.SpecificModel.ModelCode);

        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;

        requestObj["model"] = model.ModelCode;

        var latestUserMessageText = context.History
            .Where(m => !m.IsFromAi && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => ExtractTextFromMessage(m.Content))
            .LastOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (string.IsNullOrWhiteSpace(latestUserMessageText))
        {
            Logger?.LogError("Cannot prepare BFL API request: No valid user prompt text found in history for ChatSession {ChatSessionId}", context.ChatSession.Id);
            throw new InvalidOperationException("Cannot generate image without a valid user prompt text.");
        }
        requestObj["prompt"] = latestUserMessageText.Trim();

        AddParameters(requestObj, context);
        
        // Set appropriate width and height based on image_size
        // Also keep image_size for backward compatibility with the AimlApiService
        string imageSize = context.ImageSize ?? "landscape_16_9";
        requestObj["image_size"] = imageSize;
        
        int width = 1024;
        int height = 768;
        
        switch (imageSize)
        {
            case "landscape_16_9":
                width = 1024;
                height = 576;
                break;
            case "portrait_9_16":
                width = 576;
                height = 1024;
                break;
            case "square_1_1":
                width = 768;
                height = 768;
                break;
        }
        
        requestObj["width"] = width;
        requestObj["height"] = height;
        // Set num_images from context, defaulting to 1 if not specified
        var numImages = context.NumImages ?? 1;
        // Ensure num_images is between 1 and 4
        numImages = Math.Max(1, Math.Min(numImages, 4));
        requestObj["num_images"] = numImages;
        Logger?.LogInformation("Setting num_images in request to: {NumImages}", numImages);
        requestObj["output_format"] = context.OutputFormat ?? "jpeg";
        requestObj["safety_tolerance"] = context.SafetyTolerance?.ToString() ?? "2";
        // Default settings for BFL API
        requestObj["prompt_upsampling"] = false;

        Logger?.LogDebug("BFL API Payload Prepared: Model={Model}, Prompt='{Prompt}', Width={Width}, Height={Height}, Num={NumImages}, Format={Format}, Tolerance={Tolerance}",
            requestObj.GetValueOrDefault("model", "N/A"),
            requestObj.GetValueOrDefault("prompt", "N/A"),
            width,
            height,
            numImages,
            requestObj.GetValueOrDefault("output_format", "N/A"),
            requestObj.GetValueOrDefault("safety_tolerance", "N/A"));

        return Task.FromResult(new AiRequestPayload(requestObj)); 
    }
} 