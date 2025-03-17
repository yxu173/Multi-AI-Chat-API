using SharedKernel;

namespace Domain.DomainErrors;

public static class RoleErrors
{
    public static readonly Error RoleNameIsNotExist = Error.NotFound(
        "Roles.RoleNameIsNotExist",
        "The role name does not exist");
    public static readonly Error RoleNameIsExist = Error.Conflict(
        "Roles.RoleNameIsExist",
        "The role name already exists");
}