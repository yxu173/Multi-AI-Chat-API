using Application.Features.UserApiKey.CreateUserApiKey;
using Application.Features.UserApiKey.UpdateUserApiKey;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.UserApiKeys;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class UserApiKeyController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> CreateUserApiKey([Microsoft.AspNetCore.Mvc.FromBody] UserApiKeyRequest request)
    {
        var result = await new CreateUserApiKeyCommand(
            UserId,
            request.AiProviderId,
            request.ApiKey
        ).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("update/{id}")]
    public async Task<IResult> UpdateUserApiKey([FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] string UserApiKey)
    {
        var result = await new UpdateUserApiKeyCommand(UserId, id, UserApiKey).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}