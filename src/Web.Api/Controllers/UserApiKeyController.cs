using Application.Features.UserApiKey.CreateUserApiKey;
using Application.Features.UserApiKey.UpdateUserApiKey;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.UserApiKeys;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class UserApiKeyController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreateUserApiKey([FromBody] UserApiKeyRequest request)
    {
        var command = new CreateUserApiKeyCommand(
            UserId,
            request.AiProviderId,
            request.ApiKey
        );
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPut("update/{id}")]
    public async Task<IResult> UpdateUserApiKey([FromRoute] Guid id, [FromBody] string UserApiKey)
    {
        var command = new UpdateUserApiKeyCommand(UserId, id, UserApiKey);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}