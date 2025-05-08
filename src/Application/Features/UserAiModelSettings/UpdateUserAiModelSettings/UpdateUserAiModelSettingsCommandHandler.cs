using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.UserAiModelSettings.UpdateUserAiModelSettings;

public sealed class UpdateUserAiModelSettingsCommandHandler : ICommandHandler<UpdateUserAiModelSettingsCommand, bool>
{
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public UpdateUserAiModelSettingsCommandHandler(IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateUserAiModelSettingsCommand request, CancellationToken ct)
    {
        var settings = await _userAiModelSettingsRepository.GetByUserAndModelIdAsync(request.UserId, ct);
        if (settings == null)
        {
            return Result.Failure<bool>(Error.NotFound(
                "UserAiModelSettings.NotFound",
                $"User AI Model Settings for user with ID {request.UserId} not found."));
        }

        settings.UpdateSettings(
            request.AiModelId,
            request.SystemMessage,
            request.Temperature,
            request.TopP,
            request.TopK,
            request.FrequencyPenalty,
            request.PresencePenalty,
            request.ContextLimit,
            request.MaxTokens,
            request.SafetySettings,
            request.PromptCaching
        );

        await _userAiModelSettingsRepository.UpdateAsync(settings, ct);

        return Result.Success(true);
    }
}