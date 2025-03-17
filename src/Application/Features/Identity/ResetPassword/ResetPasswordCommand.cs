using Application.Abstractions.Messaging;

namespace Application.Features.Identity.ResetPassword;

public sealed record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword) : ICommand<bool>;