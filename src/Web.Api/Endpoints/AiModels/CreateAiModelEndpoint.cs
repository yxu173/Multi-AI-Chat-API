using Application.Features.AiModels.Commands.CreateAiModel;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.AiModels;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiModels;

[Authorize]
public class CreateAiModelEndpoint : Endpoint<AiModelRequest>
{
    public override void Configure()
    {
        Post("/api/aimodel/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(AiModelRequest req, CancellationToken ct)
    {
        var result = await new CreateAiModelCommand(
            req.Name,
            req.ModelType,
            req.InputTokenPricePer1M,
            req.OutputTokenPricePer1M,
            req.ModelCode,
            req.MaxOutputTokens,
            req.IsEnabled,
            req.SupportsThinking,
            req.SupportsVision,
            req.ContextLength,
            req.PromptCachingSupported,
            req.RequestCost
        ).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 