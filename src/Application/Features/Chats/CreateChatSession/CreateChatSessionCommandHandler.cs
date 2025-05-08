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

    public async Task<Result<Guid>> ExecuteAsync(CreateChatSessionCommand request, CancellationToken ct)
    {
        ChatSession chatSession;
        if (request.AiAgentId != null)
        {
            var aiAgent = await _aiAgentRepository.GetByIdAsync(request.AiAgentId.Value, ct);
            if (aiAgent == null)
                return Result.Failure<Guid>(Error.NotFound("AiAgent.NotFound", "AI Agent not found"));

            chatSession = ChatSession.Create(
                userId: request.UserId,
                aiModelId: aiAgent.ModelParameter.DefaultModel,
                folderId: request.FolderId,
                customApiKey: request.CustomApiKey,
                aiAgent: request.AiAgentId,
                enableThinking: request.EnableThinking);
            foreach (var plugin in aiAgent.AiAgentPlugins)
            {
                chatSession.AddPlugin(plugin.PluginId);
            }
        }
        else if (request.ModelId.HasValue)
        {
            chatSession = ChatSession.Create(
                request.UserId, 
                request.ModelId.Value, 
                request.FolderId, 
                request.CustomApiKey, 
                null, 
                request.EnableThinking);
        }
        else
        {
            return Result.Failure<Guid>(Error.Validation("ChatSession.InvalidInput",
                "Either ModelId or AiAgentId must be provided"));
        }

        await _chatSessionRepository.AddAsync(chatSession, ct);
        return Result.Success(chatSession.Id);
    }
}