using Application.Abstractions.Messaging;
using Domain.DomainErrors;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Identity.ResetPassword;

public class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand, bool>
{
    private readonly IUserRepository _userRepository;

    public ResetPasswordCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user is null)
            return Result.Failure<bool>(UserErrors.UserNotFound);

        var result = await _userRepository.ResetPasswordAsync(user,
            request.Token, request.NewPassword);
        if (!result)
            return Result.Failure<bool>(UserErrors.PasswordResetFailed);
        
        return Result.Success(result);
    }
}