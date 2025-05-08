using Application.Features.AiProviders.CreateAiProvider;
using Application.Features.AiProviders.DeleteAiProvider;
using Application.Features.AiProviders.GetAiProviderById;
using Application.Features.AiProviders.GetAllAiProviders;
using FastEndpoints;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.AiProviders;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class AiProviderController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> CreateAiProvider([Microsoft.AspNetCore.Mvc.FromBody] AiProviderRequest request)
    {
        var result = await new CreateAiProviderCommand(
            request.Name,
            request.Description
        ).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id:guid}")]
    public async Task<IResult> GetAiProviderById([FromRoute] Guid id)
    {
        var result = await new GetAiProviderByIdQuery(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll")]
    public async Task<IResult> GetAllAiProviders()
    {
        var result = await new GetAllAiProvidersQuery().ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id:guid}")]
    public async Task<IResult> DeleteAiProvder([FromRoute] Guid id)
    {
        var result = await new DeleteAiProviderCommand(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}