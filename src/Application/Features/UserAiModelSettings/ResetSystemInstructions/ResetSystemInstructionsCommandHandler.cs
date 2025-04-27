using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.UserAiModelSettings.ResetSystemInstructions;

public sealed class ResetSystemInstructionsCommandHandler : ICommandHandler<ResetSystemInstructionsCommand, bool>
{
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public ResetSystemInstructionsCommandHandler(IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
    }

    public async Task<Result<bool>> Handle(ResetSystemInstructionsCommand request, CancellationToken cancellationToken)
    {
        var setting = await _userAiModelSettingsRepository.GetByUserAndModelIdAsync(request.UserId, cancellationToken);

        if (setting == null)
            return Result.Failure<bool>(Error.NotFound(
                "User settings not found.",
                "User settings don't exist"));

        setting.ModelParameters.ResetSystemInstructions();

        await _userAiModelSettingsRepository.UpdateAsync(setting, cancellationToken);

        return Result.Success(true);
    }
}