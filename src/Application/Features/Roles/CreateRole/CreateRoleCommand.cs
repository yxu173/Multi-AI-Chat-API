using Application.Abstractions.Messaging;

namespace Application.Features.Roles.CreateRole;

public record CreateRoleCommand(string RoleName) : ICommand<bool>;