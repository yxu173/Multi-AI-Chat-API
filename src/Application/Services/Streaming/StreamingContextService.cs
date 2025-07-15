using Application.Exceptions;
using Application.Services.AI;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;

namespace Application.Services.Streaming;

public class StreamingContextService : IStreamingContextService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IToolDefinitionService _toolDefinitionService;
    private readonly IMessageRepository _messageRepository;

    public StreamingContextService(
        IChatSessionRepository chatSessionRepository,
        IUserAiModelSettingsRepository userAiModelSettingsRepository,
        IAiAgentRepository aiAgentRepository,
        IToolDefinitionService toolDefinitionService,
        IMessageRepository messageRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
        _aiAgentRepository = aiAgentRepository;
        _toolDefinitionService = toolDefinitionService;
        _messageRepository = messageRepository;
    }

    public async Task<AiRequestContext> BuildContextAsync(StreamingRequest request, CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(request.ChatSessionId)
                          ?? throw new NotFoundException(nameof(ChatSession), request.ChatSessionId);

        var aiMessage = chatSession.Messages.FirstOrDefault(m => m.Id == request.AiMessageId)
                        ?? throw new NotFoundException(nameof(Message), request.AiMessageId);

        var userSettings = await _userAiModelSettingsRepository.GetDefaultByUserIdAsync(request.UserId, cancellationToken);

        var aiAgent = chatSession.AiAgentId.HasValue
            ? await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken)
            : null;

        var toolDefinitions = await _toolDefinitionService.GetToolDefinitionsAsync(
            request.UserId, 
            request.EnableDeepSearch, 
            chatSession.AiModel.ModelType,
            cancellationToken);

        var history = request.History is not null && request.History.Any()
            ? HistoryBuilder.BuildHistory(request.History.Select(m => MessageDto.FromEntity(m)).ToList())
            : HistoryBuilder.BuildHistory(chatSession, MessageDto.FromEntity(aiMessage));

        return new AiRequestContext(
            UserId: request.UserId,
            ChatSession: chatSession,
            History: history,
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking: request.EnableThinking,
            ImageSize: request.ImageSize,
            NumImages: request.NumImages,
            OutputFormat: request.OutputFormat,
            EnableSafetyChecker: request.EnableSafetyChecker,
            SafetyTolerance: request.SafetyTolerance,
            ToolDefinitions: toolDefinitions,
            EnableDeepSearch: request.EnableDeepSearch);
    }
} 