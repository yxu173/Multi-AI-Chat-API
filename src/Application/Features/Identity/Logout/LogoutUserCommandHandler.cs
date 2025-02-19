using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Application.Features.Identity.Logout;

public class LogoutUserCommandHandler(SignInManager<User> signInManager) : ICommandHandler<LogoutUserCommand, bool>
{
    public async Task<Result<bool>> Handle(LogoutUserCommand request, CancellationToken cancellationToken)
    {
        await signInManager.SignOutAsync();
        return Result.Success(true);
    }
}