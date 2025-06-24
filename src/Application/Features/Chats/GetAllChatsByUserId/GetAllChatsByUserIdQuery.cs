using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;

namespace Application.Features.Chats.GetAllChatsByUserId;

public sealed record GetAllChatsByUserIdQuery(Guid UserId, int Page = 1, int PageSize = 20) : IQuery<GetAllChatsWithFoldersDto>;