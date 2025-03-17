using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.DomainErrors;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Application.Features.Identity.Login;

public sealed class LoginUserCommandHandler(
    IUserRepository userRepository,
    ITokenProvider tokenProvider)
    : ICommandHandler<LoginUserCommand, string>
{
    public async Task<Result<string>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(request.Email);
        if (user == null)
        {
            return Result.Failure<string>(UserErrors.NotFoundByEmail);
        }

        var result = await userRepository.CheckPasswordAsync(user, request.Password);
        if (!result)
        {
            return Result.Failure<string>(UserErrors.NotFoundByEmail);
        }

        var token = tokenProvider.Create(user);

        return Result.Success(token);
    }
}