using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Application.Features.Roles.CreateRole;

public class CreateRoleCommandHandler(IApplicationDbContext context, RoleManager<Role> roleManager) : ICommandHandler<CreateRoleCommand, bool>
{
    private readonly IApplicationDbContext _context = context;
    private readonly RoleManager<Role> _roleManager = roleManager;
    public async Task<Result<bool>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
       var isExist = await _roleManager.RoleExistsAsync(request.RoleName);
       if (isExist)
       {
           return Result.Failure<bool>(RoleErrors.RoleNameIsExist);
       }
        var role = Role.Create(request.RoleName);
       await _roleManager.CreateAsync(role);
        return Result.Success(true);
    }
}