using Application.Features.Roles.CreateRole;
using Application.Features.Roles.DeleteRole;
using FastEndpoints;
using Web.Api.Contracts.Roles;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class RoleController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("CreateRole")]
    public async Task<IResult> CreateRole([Microsoft.AspNetCore.Mvc.FromBody] RoleCreate model)
    {
        var result = await new CreateRoleCommand(model.Name).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
    [Microsoft.AspNetCore.Mvc.HttpDelete("DeleteRole")]
    public async Task<IResult> DeleteRole([Microsoft.AspNetCore.Mvc.FromBody] RoleDelete model)
    {
        var result = await new DeleteRoleCommand(model.Name).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}