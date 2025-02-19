using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Repositories;
using Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Features.Identity.Register;

internal sealed class RegisterUserCommandHandler(
    IApplicationDbContext context,
    IUserRepository userRepository)
    : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (await userRepository.ExistsByEmailAsync(command.Email))
        {
            return Result.Failure<Guid>(UserErrors.EmailNotUnique);
        }

        var user = User.Create( command.Email, command.UserName);
        if (user.IsFailure)
        {
            return Result.Failure<Guid>(user.Error);
        }

        var result = await userRepository.CreateAsync(user.Value, command.Password);
        //var addRoleResult = await userManager.AddToRoleAsync(user.Value, Roles.Basic.ToString());
        if (!result.Succeeded)
        {
            return Result.Failure<Guid>(UserErrors.RegisterUserError);
        }

        return user.Value.Id;
    }
}