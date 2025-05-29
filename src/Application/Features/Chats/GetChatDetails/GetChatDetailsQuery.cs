using Application.Abstractions.Messaging;

namespace Application.Features.Chats.GetChatDetails;

public sealed record GetChatDetailsQuery(Guid UserId, Guid ChatId) : IQuery<ChatDetailsResponse>;