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

    public async Task<Result<bool>> Handle(UpdateUserAiModelSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var settings = await _userAiModelSettingsRepository.GetByUserAndModelIdAsync(request.UserId, cancellationToken);
        if (settings == null)
        {
            return Result.Failure<bool>(Error.NotFound(
                "UserAiModelSettings.NotFound",
                $"User AI Model Settings for user with ID {request.UserId} not found."));
        }

        settings.UpdateSettings(
            request.Temperature,
            request.TopP,
            request.TopK,
            request.FrequencyPenalty,
            request.PresencePenalty
        );

        await _userAiModelSettingsRepository.UpdateAsync(settings, cancellationToken);

        return Result.Success(true);
    }
}