namespace Web.Api.Contracts.ChatFolders;

public sealed record CreateFolderRequest(string Name, string? Description = null);