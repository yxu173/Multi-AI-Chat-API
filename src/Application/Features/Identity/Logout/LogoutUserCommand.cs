using Application.Abstractions.Messaging;

namespace Application.Features.Identity.Logout;

public sealed record LogoutUserCommand() : ICommand<bool>;