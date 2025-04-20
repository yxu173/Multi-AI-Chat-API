using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.PayloadBuilders;

public class AimlFluxPayloadBuilder : BasePayloadBuilder, IAimlFluxPayloadBuilder
{
    public AimlFluxPayloadBuilder(ILogger<AimlFluxPayloadBuilder> logger)
        : base(logger)
    {
    }

    public AiRequestPayload PreparePayload(AiRequestContext context)
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

        if (IsParameterSupported("image_size", ModelType.AimlFlux))
            requestObj["image_size"] = context.ImageSize ?? "landscape_16_9"; 
        if (IsParameterSupported("num_images", ModelType.AimlFlux))
             requestObj["num_images"] = context.NumImages ?? 1; 
        if (IsParameterSupported("output_format", ModelType.AimlFlux))
            requestObj["output_format"] = context.OutputFormat ?? "jpeg"; 
        if (IsParameterSupported("enable_safety_checker", ModelType.AimlFlux))
            requestObj["enable_safety_checker"] = context.EnableSafetyChecker ?? true; 
        if (IsParameterSupported("safety_tolerance", ModelType.AimlFlux))
             requestObj["safety_tolerance"] = context.SafetyTolerance ?? "2";
        

        Logger?.LogDebug("AIMLAPI Payload Prepared: Model={Model}, Prompt='{Prompt}', Size={ImageSize}, Num={NumImages}, Format={Format}, Safety={Safety}, Tolerance={Tolerance}",
            requestObj.GetValueOrDefault("model", "N/A"),
            requestObj.GetValueOrDefault("prompt", "N/A"),
            requestObj.GetValueOrDefault("image_size", "N/A"),
            requestObj.GetValueOrDefault("num_images", "N/A"),
            requestObj.GetValueOrDefault("output_format", "N/A"),
            requestObj.GetValueOrDefault("enable_safety_checker", "N/A"),
            requestObj.GetValueOrDefault("safety_tolerance", "N/A"));

        return new AiRequestPayload(requestObj);
    }

    private string ExtractTextFromMessage(string? messageContent)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
        {
            return string.Empty;
        }
       var textOnly = System.Text.RegularExpressions.Regex.Replace(messageContent,
             @"<(image|file)-base64:(?:[^:]*?:)?([^;]*?);base64,([^>]*?)>",
             string.Empty,
             System.Text.RegularExpressions.RegexOptions.IgnoreCase);
             
        return textOnly.Trim();
    }
} 