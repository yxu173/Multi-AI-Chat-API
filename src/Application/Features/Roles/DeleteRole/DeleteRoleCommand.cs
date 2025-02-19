using Application.Abstractions.Messaging;

namespace Application.Features.Roles.DeleteRole;

public record DeleteRoleCommand(string RoleName) : ICommand<bool>;