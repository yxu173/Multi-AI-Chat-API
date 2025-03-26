using Microsoft.AspNetCore.SignalR;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Domain.Repositories;
using MediatR;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Domain.Aggregates.Chats;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IMediator _mediator;
    private readonly MessageService _messageService;

    public ChatHub(
        ChatService chatService,
        StreamingOperationManager streamingOperationManager,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IMediator mediator,
        MessageService messageService)
    {
        _chatService = chatService;
        _streamingOperationManager = streamingOperationManager;
        _messageRepository = messageRepository;
        _fileAttachmentRepository = fileAttachmentRepository;
        _mediator = mediator;
        _messageService = messageService;
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

    public async Task SendMessage(string chatSessionId, string content, IEnumerable<string> fileIds = null)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        var fileAttachments = new List<FileAttachment>();

        if (fileIds != null)
        {
            foreach (var fileIdStr in fileIds)
            {
                if (Guid.TryParse(fileIdStr, out Guid fileId))
                {
                    var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, CancellationToken.None);
                    if (fileAttachment != null)
                    {
                        fileAttachments.Add(fileAttachment);
                    }
                }
            }
        }

        await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content, fileAttachments);
    }

    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId), newContent);
    }
}