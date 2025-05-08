using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Application.Features.Identity.Logout;

public class LogoutUserCommandHandler(SignInManager<User> signInManager) : ICommandHandler<LogoutUserCommand, bool>
{
    public async Task<Result<bool>> ExecuteAsync(LogoutUserCommand command, CancellationToken ct)
    {
        await signInManager.SignOutAsync();
        return Result.Success(true);
    }
}