using Application.Abstractions.Messaging;

namespace Application.Features.Identity.Login;

public sealed record LoginUserCommand(string Email , string Password) : ICommand<string>;