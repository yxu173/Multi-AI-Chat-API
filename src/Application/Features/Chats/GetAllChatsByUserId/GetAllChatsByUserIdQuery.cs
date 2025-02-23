using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;

namespace Application.Features.Chats.GetAllChatsByUserId;

public sealed record GetAllChatsByUserIdQuery(Guid UserId) : IQuery<IReadOnlyList<GetAllChatsDto>>;