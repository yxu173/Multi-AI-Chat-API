using Application.Features.AiProviders.CreateAiProvider;
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
}