using Application.Features.Roles.CreateRole;
using Application.Features.Roles.DeleteRole;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Roles;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class RoleController : BaseController
{
    [HttpPost("CreateRole")]
    public async Task<IResult> CreateRole([FromBody] RoleCreate model)
    {
        var command = new CreateRoleCommand(model.Name);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
    [HttpDelete("DeleteRole")]
    public async Task<IResult> DeleteRole([FromBody] RoleDelete model)
    {
        var command = new DeleteRoleCommand(model.Name);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}