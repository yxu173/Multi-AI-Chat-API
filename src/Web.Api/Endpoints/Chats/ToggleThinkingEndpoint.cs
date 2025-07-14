using Application.Features.Chats.UpdateChatSession;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.Chats;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class ToggleThinkingRequest
{
    public bool Enable { get; set; }
}

public class ToggleThinkingEndpoint : Endpoint<ToggleThinkingRequest>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAiModelRepository _aiModelRepository;
    public ToggleThinkingEndpoint(IChatSessionRepository chatSessionRepository, IAiModelRepository aiModelRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _aiModelRepository = aiModelRepository;
    }

    public override void Configure()
    {
        Post("/api/chat/ToggleThinking/{ChatId}");
        Description(x => x.Produces(200).Produces(400).Produces(403).Produces(500));
    }

    public override async Task HandleAsync(ToggleThinkingRequest req, CancellationToken ct)
    {
        var chatIdStr = Route<string>("ChatId");
        if (!Guid.TryParse(chatIdStr, out var chatId))
        {
            await SendErrorsAsync(400, ct);
            return;
        }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        try
        {
            var session = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(chatId);
            if (session.UserId != Guid.Parse(userId))
            {
                await SendForbiddenAsync(ct);
                return;
            }
            var aiModel = await _aiModelRepository.GetByIdAsync(session.AiModelId);
            if (req.Enable && (aiModel == null || !aiModel.SupportsThinking))
            {
                await SendAsync(new { Message = "This AI model does not support thinking mode", AiModelName = aiModel?.Name ?? "Unknown model" }, 400, ct);
                return;
            }
            session.ToggleThinking(req.Enable);
            await new UpdateChatSessionCommand(chatId, session.Title, session.FolderId).ExecuteAsync();
            await SendOkAsync(new { Enabled = req.Enable }, ct);
        }
        catch (Exception ex)
        {
            await SendAsync(new { Message = ex.Message }, 500, ct);
        }
    }
} 