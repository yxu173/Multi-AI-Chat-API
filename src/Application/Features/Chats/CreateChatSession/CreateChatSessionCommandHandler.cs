using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.CreateChatSession;

public class CreateChatSessionCommandHandler : ICommandHandler<CreateChatSessionCommand, Guid>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAiAgentRepository _aiAgentRepository;

    public CreateChatSessionCommandHandler(IChatSessionRepository chatSessionRepository,
        IAiAgentRepository aiAgentRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task<Result<Guid>> Handle(CreateChatSessionCommand request, CancellationToken cancellationToken)
    {
        ChatSession chatSession;
        if (request.AiAgentId != null)
        {
            var aiAgent = await _aiAgentRepository.GetByIdAsync(request.AiAgentId.Value, cancellationToken);
            if (aiAgent == null)
                return Result.Failure<Guid>(Error.NotFound("AiAgent.NotFound", "AI Agent not found"));

            chatSession = ChatSession.Create(
                userId: request.UserId,
                aiModelId: aiAgent.AiModelId,
                folderId: request.FolderId,
                aiAgent: request.AiAgentId);
            chatSession.SetSystemPrompt(aiAgent.SystemPrompt);
            foreach (var plugin in aiAgent.AiAgentPlugins)
            {
                chatSession.AddPlugin(plugin.PluginId, plugin.Order);
            }
        }
        else if (request.ModelId.HasValue)
        {
            chatSession = ChatSession.Create(request.UserId, request.ModelId.Value, request.FolderId);
        }
        else
        {
            return Result.Failure<Guid>(Error.Validation("ChatSession.InvalidInput",
                "Either ModelId or AiAgentId must be provided"));
        }

        await _chatSessionRepository.AddAsync(chatSession, cancellationToken);
        return Result.Success(chatSession.Id);
    }
}