using Microsoft.AspNetCore.SignalR;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Application.Services;
using System.Text;
using Application.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;

    public ChatHub(ChatService chatService)
    {
        _chatService = chatService;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChatSession(string chatSessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
    }

    public async Task SendMessage(string chatSessionId, string content)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content);
    }

    public async Task SubscribeToSearch(string searchTerm)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"search:{searchTerm}");
    }
}