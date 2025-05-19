using Application.Abstractions.Messaging;
using Domain.Aggregates.Users;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiModels.Commands.UserEnableAiModel;

public sealed class UserEnableAiModelCommandHandler : ICommandHandler<UserEnableAiModelCommand, bool>
{
    private readonly IAiModelRepository _aiModelRepository;

    public UserEnableAiModelCommandHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(UserEnableAiModelCommand request, CancellationToken ct)
    {
        var userAiModel = await _aiModelRepository.GetUserAiModelAsync(request.UserId, request.AiModelId);

        if (userAiModel is null)
        {
            userAiModel = UserAiModel.Create(request.UserId, request.AiModelId);
            await _aiModelRepository.AddUserAiModelAsync(userAiModel);
        }

        var aiModel = await _aiModelRepository.GetByIdAsync(request.AiModelId);
        if (userAiModel.IsEnabled == true)
        {
            userAiModel.SetEnabled(false);
        }
        else
        {
            userAiModel.SetEnabled(true);
        }

        await _aiModelRepository.UpdateAsync(aiModel);
        return Result.Success(true);
    }
}