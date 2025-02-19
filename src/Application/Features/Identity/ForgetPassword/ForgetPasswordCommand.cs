using Application.Abstractions.Messaging;

namespace Application.Features.Identity.ForgetPassword;

public sealed record ForgetPasswordCommand(string Email) : ICommand<bool>;
