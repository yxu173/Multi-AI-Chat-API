using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.UserAiModelSettings.GetUserAiModelSettings;

public sealed class
    GetUserAiModelSettingsCommandHandler : ICommandHandler<GetUserAiModelSettingsCommand, UserAiModelSettingsResponse>
{
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public GetUserAiModelSettingsCommandHandler(IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
    }

    public async Task<Result<UserAiModelSettingsResponse>> Handle(GetUserAiModelSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var settings = await _userAiModelSettingsRepository.GetByUserAndModelIdAsync(request.UserId, cancellationToken);
        if (settings == null)
        {
            return Result.Failure<UserAiModelSettingsResponse>(Error.NotFound(
                "UserAiModelSettings.NotFound",
                $"User AI Model Settings for user with ID {request.UserId} not found."));
        }

        return new UserAiModelSettingsResponse(
            settings.Id,
            settings.UserId,
            settings.SystemMessage,
            settings.Temperature,
            settings.TopP,
            settings.TopK,
            settings.FrequencyPenalty,
            settings.PresencePenalty
        );
    }
}