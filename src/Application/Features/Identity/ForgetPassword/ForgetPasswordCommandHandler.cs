using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.DomainErrors;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Identity.ForgetPassword;

public class ForgetPasswordCommandHandler : ICommandHandler<ForgetPasswordCommand, bool>
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailSender _emailSender;

    public ForgetPasswordCommandHandler(IUserRepository userRepository, IEmailSender emailSender)
    {
        _userRepository = userRepository;
        _emailSender = emailSender;
    }

    public async Task<Result<bool>> Handle(ForgetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user == null)
            return Result.Failure<bool>(UserErrors.UserNotFound);
        var token = await _userRepository.GeneratePasswordResetTokenAsync(user);
        var resetLink = $"http://localhost:3000/reset-password?token={token}&email={request.Email}";
        
        await _emailSender.SendEmailAsync(request.Email, "Reset Password", 
            $"Click on the following link to reset your password: {resetLink}");
        return Result.Success(true);
    }
}