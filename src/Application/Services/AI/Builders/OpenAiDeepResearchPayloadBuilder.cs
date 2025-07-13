using Application.Abstractions.Interfaces;
using Application.Services.AI.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.AI.Builders;

public class OpenAiDeepResearchPayloadBuilder : IAiRequestBuilder
{
    private readonly ILogger<OpenAiDeepResearchPayloadBuilder> _logger;

    public OpenAiDeepResearchPayloadBuilder(ILogger<OpenAiDeepResearchPayloadBuilder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<PluginDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Preparing payload for OpenAI Deep Research model {ModelCode}",
            context.SpecificModel.ModelCode);

        var requestObj = new Dictionary<string, object>();
        var model = context.SpecificModel;
        requestObj["background"] = true;
        requestObj["model"] = model.ModelCode;

        var lastUserMessage = context.History.LastOrDefault(m => !m.IsFromAi && !string.IsNullOrWhiteSpace(m.Content));
        if (lastUserMessage is not null)
        {
            requestObj["input"] = lastUserMessage.Content!.Trim();
        }
        else
        {
            _logger.LogWarning(
                "No user message found in history for deep research request for ChatSession {ChatSessionId}",
                context.ChatSession.Id);
            requestObj["input"] = "";
        }
        requestObj["stream"] = true; 

      
        var deepResearchTools = new List<object>();
        

        deepResearchTools.Add(new { type = "web_search_preview" });
        _logger.LogInformation("Enabling 'web_search_preview' tool for deep research.");
        
        if (tools?.Any(t => t.Name.Equals("code_interpreter", StringComparison.OrdinalIgnoreCase)) == true)
        {
            deepResearchTools.Add(new { type = "code_interpreter", container = new { type = "auto" } });
            _logger.LogInformation("Enabling 'code_interpreter' tool for deep research.");
        }

        if (deepResearchTools.Any())
        {
            requestObj["tools"] = deepResearchTools;
        }
        else
        {
            _logger.LogWarning(
                "No tools specified for deep research model {ModelCode}. The API may reject this request as it requires at least one data source.",
                model.ModelCode);
        }
        
        return Task.FromResult(new AiRequestPayload(requestObj));
    }
}