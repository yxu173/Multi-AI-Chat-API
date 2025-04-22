using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.UserAiModelSettings.GetUserAiModelSettings;

public sealed class
    GetUserAiModelSettingsCommandHandler : ICommandHandler<GetUserAiModelSettingsCommand, UserAiModelSettingsResponse>
{
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;
    private readonly IAiModelRepository _aiModelRepository;

    public GetUserAiModelSettingsCommandHandler(IUserAiModelSettingsRepository userAiModelSettingsRepository, IAiModelRepository aiModelRepository)
    {
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
        _aiModelRepository = aiModelRepository;
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

        var modelName = await _aiModelRepository.GetModelNameById(settings.ModelParameters.DefaultModel);

        return new UserAiModelSettingsResponse(
            settings.Id,
            settings.UserId,
            modelName,
            settings.ModelParameters.SystemInstructions,
            settings.ModelParameters.ContextLimit,
            settings.ModelParameters.Temperature,
            settings.ModelParameters.TopP,
            settings.ModelParameters.TopK,
            settings.ModelParameters.FrequencyPenalty,
            settings.ModelParameters.PresencePenalty,
            settings.ModelParameters.MaxTokens,
            settings.ModelParameters.SafetySettings,
            settings.ModelParameters.PromptCaching
        );
    }
}