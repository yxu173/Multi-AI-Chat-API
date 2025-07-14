using Application.Features.Roles.DeleteRole;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Roles;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Roles;

[Authorize]
public class DeleteRoleEndpoint : Endpoint<RoleDelete>
{
    public override void Configure()
    {
        Delete("/api/role/DeleteRole");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(RoleDelete req, CancellationToken ct)
    {
        var result = await new DeleteRoleCommand(req.Name).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
}