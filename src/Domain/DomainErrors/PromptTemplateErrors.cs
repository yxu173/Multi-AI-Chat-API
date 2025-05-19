using SharedKernal;

namespace Domain.DomainErrors;

public static class PromptTemplateErrors
{
    public static readonly Error PromptTemplateNotFound = Error.NotFound(
        "PromptTemplates.PromptTemplateNotFound",
        "The prompt template was not found");

    public static readonly Error PromptTemplateNotCreated = Error.Failure(
        "PromptTemplates.PromptTemplateNotCreated",
        "Failed to create prompt template");

    public static readonly Error PromptTemplateNotUpdated = Error.Failure(
        "PromptTemplates.PromptTemplateNotUpdated",
        "Failed to update prompt template");

    public static readonly Error PromptTemplateNotDeleted = Error.Failure(
        "PromptTemplates.PromptTemplateNotDeleted",
        "Failed to delete prompt template");

    public static readonly Error TagNotValid = Error.BadRequest(
        "PromptTemplates.TagNotValid",
        "The provided tag is not valid");

    public static readonly Error TagIsAlreadyExist = Error.Conflict(
        "PromptTemplates.TagIsAlreadyExist",
        "The tag already exists");

    public static readonly Error TagNotFound = Error.NotFound(
        "PromptTemplates.TagNotFound",
        "The tag was not found");

    public static readonly Error UserIsNotAuthorized = Error.Failure(
        "PromptTemplates.UserIsNotAuthorized",
        "The user is not authorized to perform this action");
}