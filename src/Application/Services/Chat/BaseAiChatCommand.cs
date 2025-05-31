using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Domain.Aggregates.Admin;

namespace Application.Services.Chat;

public abstract class BaseAiChatCommand
{
    protected readonly IAiModelServiceFactory AiModelServiceFactory;
    protected readonly IAiAgentRepository AiAgentRepository;
    protected readonly IUserAiModelSettingsRepository UserAiModelSettingsRepository;

    protected BaseAiChatCommand(
        IAiModelServiceFactory aiModelServiceFactory,
        IAiAgentRepository aiAgentRepository,
        IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        AiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        AiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        UserAiModelSettingsRepository = userAiModelSettingsRepository ?? throw new ArgumentNullException(nameof(userAiModelSettingsRepository));
    }

    protected async Task<(IAiModelService AiService, ProviderApiKey? ApiKey, AiAgent? AiAgent)> PrepareForAiInteractionAsync(
        Guid userId,
        ChatSession chatSession,
        CancellationToken cancellationToken)
    {
        var serviceContext = await AiModelServiceFactory.GetServiceContextAsync(userId, chatSession.AiModelId, chatSession.AiAgentId, cancellationToken);
        AiAgent? aiAgent = null;
        if (chatSession.AiAgentId.HasValue)
        {
            aiAgent = await AiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken);
        }
        return (serviceContext.Service, serviceContext.ApiKey, aiAgent);
    }
}
