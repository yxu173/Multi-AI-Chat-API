using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Domain.DomainErrors;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Features.Identity.Register;

internal sealed class RegisterUserCommandHandler(
    UserManager<User> userManager,
    IUserRepository userRepository)
    : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (await userRepository.ExistsByEmailAsync(command.Email))
        {
            return Result.Failure<Guid>(UserErrors.EmailNotUnique);
        }

        var user = User.Create(command.Email, command.UserName);
        if (user.IsFailure)
        {
            return Result.Failure<Guid>(user.Error);
        }

        var result = await userManager.CreateAsync(user.Value, command.Password);
        if (!result.Succeeded)
        {
            return Result.Failure<Guid>(UserErrors.RegisterUserError);
        }

        return user.Value.Id;
    }
}