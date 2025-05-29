using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.UserAiModelSettings.ResetSystemInstructions;

public sealed class ResetSystemInstructionsCommandHandler : ICommandHandler<ResetSystemInstructionsCommand, bool>
{
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public ResetSystemInstructionsCommandHandler(IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        _userAiModelSettingsRepository = userAiModelSettingsRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(ResetSystemInstructionsCommand command, CancellationToken ct)
    {
        var setting = await _userAiModelSettingsRepository.GetByUserAndModelIdAsync(command.UserId, ct);

        if (setting == null)
            return Result.Failure<bool>(Error.NotFound(
                "User settings not found.",
                "User settings don't exist"));

        // Create a new ModelParameters instance with default system instructions
        var updatedParameters = setting.ModelParameters.WithDefaultSystemInstructions();
        
        // Update the setting with the new parameters
        setting.UpdateModelParameters(updatedParameters);

        await _userAiModelSettingsRepository.UpdateAsync(setting, ct);

        return Result.Success(true);
    }
}