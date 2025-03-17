using SharedKernel;

namespace Domain.DomainErrors;

public static class ChatErrors
{
    public static readonly Error ChatNotFound = Error.NotFound(
        "Chats.ChatNotFound",
        "The chat was not found");
    
    public static readonly Error ChatNotCreated = Error.Failure(
        "Chats.ChatNotCreated",
        "Failed to create chat");
    public static readonly Error UserIdNotValid = Error.BadRequest(
        "Chats.UserIdNotValid",
        "The provided user id is not valid");
    
    public static readonly Error ChatNotUpdated = Error.Failure(
        "Chats.ChatNotUpdated",
        "Failed to update chat");
    public static readonly Error ChatNotDeleted = Error.Failure(
        "Chats.ChatNotDeleted",
        "Failed to delete chat");
    public static readonly Error MessageNotEmpty = Error.Failure(
        "Chats.MessageNotAdded",
        "Failed to add message to chat");
}