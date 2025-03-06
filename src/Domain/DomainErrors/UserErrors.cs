using SharedKernel;

namespace Domain.DomainErrors;

public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        "Users.NotFound",
        $"The user with the Id = '{userId}' was not found");


    public static Error Unauthorized() => Error.Failure(
        "Users.Unauthorized",
        "You are not authorized to perform this action.");

    public static readonly Error NotFoundByEmail = Error.NotFound(
        "Users.NotFoundByEmail",
        "The user with the specified email was not found");

    public static readonly Error UserNotFound = Error.NotFound(
        "Users.NotFound",
        "The user was not found");


    public static readonly Error EmailNotUnique = Error.Conflict(
        "Users.EmailNotUnique",
        "The provided email is not unique");

    public static readonly Error LockedOut = Error.Failure(
        "Users.LockedOut",
        "The user account is locked out");

    public static readonly Error GoogleLoginFailed = Error.Failure(
        "Users.GoogleLoginFailed",
        "Google login failed");

    public static readonly Error RegisterUserError = Error.Failure(
        "Users.RegisterUserError",
        "Failed to register user");

    public static readonly Error PasswordResetFailed = Error.Failure(
        "Users.PasswordResetFailed",
        "Failed to reset password");

    public static readonly Error AiModelIsAlreadyExist = Error.Conflict(
        "Users.AiModelIsAlreadyExist",
        "AiModel is already exist"
    );
}