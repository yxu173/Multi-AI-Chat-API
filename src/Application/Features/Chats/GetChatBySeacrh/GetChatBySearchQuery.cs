using Application.Abstractions.Messaging;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;

namespace Application.Features.Chats.GetChatBySeacrh;

public sealed record GetChatBySearchQuery(Guid UserId, string Search) : IQuery<IEnumerable<ChatSearchResultDto>>;