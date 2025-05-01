using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.GetChatDetails;

public sealed class GetChatDetailsQueryHandler : IQueryHandler<GetChatDetailsQuery, ChatDetailsResponse>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatTokenUsageRepository _chatTokenUsageRepository;

    public GetChatDetailsQueryHandler(IChatSessionRepository chatSessionRepository,
        IChatTokenUsageRepository chatTokenUsageRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _chatTokenUsageRepository = chatTokenUsageRepository;
    }

    public async Task<Result<ChatDetailsResponse>> Handle(GetChatDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var chat = await _chatSessionRepository.GetChatWithModel(request.ChatId);

        if (chat.UserId != request.UserId)
        {
            return Result.Failure<ChatDetailsResponse>(Error.NotFound(
                "Chat not found.",
                "The chat you are trying to get does not exist."
            ));
        }

        var ChatTokens = await _chatTokenUsageRepository.GetByChatSessionIdAsync(request.ChatId);

        var response = new ChatDetailsResponse
        (
            chat.CreatedAt,
            chat.AiModel.Name,
            GetCurrentContextLength(chat.AiModel.ContextLength, ChatTokens.InputTokens),
            ChatTokens.InputTokens,
            ChatTokens.OutputTokens,
            ChatTokens.TotalCost
        );

        return Result.Success(response);
    }

    private string GetCurrentContextLength(int contextLength, int inputTokens)
    {
        float percent = (inputTokens / contextLength) * 100;
        return $"{inputTokens} tokens({percent}%) Estimated";
    }
}