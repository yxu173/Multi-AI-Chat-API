using SharedKernel;

namespace Domain.DomainErrors;

public static class ApiKeyErrors
{
    public static readonly Error ValidUserId = Error.Validation(
        "ApiKey.UserIdNotValid",
        "ApiKey.User Id doesn't match your Id");
}