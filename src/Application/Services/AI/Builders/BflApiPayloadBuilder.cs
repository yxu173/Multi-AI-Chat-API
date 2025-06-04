using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.AI.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Builders;

public class BflApiPayloadBuilder : BasePayloadBuilder, IAiRequestBuilder
{
    public BflApiPayloadBuilder(ILogger<BflApiPayloadBuilder> logger)
        : base(logger)
    {
    }

    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<object>? tools = null, CancellationToken cancellationToken = default)
    {
        Logger?.LogInformation("Preparing payload for BFL API model {ModelCode}", context.SpecificModel.ModelCode);

        var requestObj = new Dictionary<string, object>();

        // Get the latest user message as the prompt
        var latestUserMessageText = context.History
            .Where(m => !m.IsFromAi && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => ExtractTextFromMessage(m.Content))
            .LastOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (string.IsNullOrWhiteSpace(latestUserMessageText))
        {
            Logger?.LogError("Cannot prepare BFL API request: No valid user prompt text found in history for ChatSession {ChatSessionId}", context.ChatSession.Id);
            throw new InvalidOperationException("Cannot generate image without a valid user prompt text.");
        }

        // Set the prompt
        requestObj["prompt"] = latestUserMessageText.Trim();
        requestObj["safety_tolerance"] = context.SafetyTolerance ?? 2;
       requestObj["output_format"] = context.OutputFormat ?? "jpeg"; 
        // requestObj["image_size"] = context.ImageSize ?? "1024x1024";

        // Add any additional parameters from context
        AddParameters(requestObj, context);

        Logger?.LogDebug("BFL API Payload Prepared: Prompt='{Prompt}'", 
            requestObj.GetValueOrDefault("prompt", "N/A"));

        return Task.FromResult(new AiRequestPayload(requestObj));
    }

    protected override void CustomizePayload(Dictionary<string, object> requestObj, AiRequestContext context)
    {
        // Remove any parameters that are not supported by BFL API
        requestObj.Remove("temperature");
        requestObj.Remove("top_p");
        requestObj.Remove("top_k");
        requestObj.Remove("max_tokens");
        requestObj.Remove("frequency_penalty");
        requestObj.Remove("presence_penalty");
    }
} 