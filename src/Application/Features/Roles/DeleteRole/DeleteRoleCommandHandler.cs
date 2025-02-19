using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Domain.DomainErrors;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Application.Features.Roles.DeleteRole;

public class DeleteRoleCommandHandler(RoleManager<Role> roleManager) : ICommandHandler<DeleteRoleCommand, bool>
{
    public async Task<Result<bool>> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(request.RoleName);
        if (role == null)
        {
            return Result.Failure<bool>(RoleErrors.RoleNameIsNotExist);
        }
        var result = await roleManager.DeleteAsync(role);
        return Result.Success(result.Succeeded);
    }
}