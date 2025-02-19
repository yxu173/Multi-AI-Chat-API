
using Application.Abstractions.Messaging;

namespace Application.Features.Identity.Register;

public sealed record RegisterUserCommand(string UserName , string Email, string Password)
    : ICommand<Guid>;