using Application.Features.AiProviders.CreateAiProvider;
using Application.Features.AiProviders.DeleteAiProvider;
using Application.Features.AiProviders.GetAiProviderById;
using Application.Features.AiProviders.GetAllAiProviders;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.AiProviders;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class AiProviderController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreateAiProvider([FromBody] AiProviderRequest request)
    {
        var command = new CreateAiProviderCommand(
            request.Name,
            request.Description
        );
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("{id:guid}")]
    public async Task<IResult> GetAiProviderById([FromRoute] Guid id)
    {
        var query = new GetAiProviderByIdQuery(id);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetAll")]
    public async Task<IResult> GetAllAiProviders()
    {
        var query = new GetAllAiProvidersQuery();
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IResult> DeleteAiProvder([FromRoute] Guid id)
    {
        var command = new DeleteAiProviderCommand(id);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}