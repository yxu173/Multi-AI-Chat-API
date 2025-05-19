using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiModels.Commands.EnableAiModel;

public sealed class EnableAiModelCommandHandler : ICommandHandler<EnableAiModelCommand, bool>
{
    private readonly IAiModelRepository _aiModelRepository;

    public EnableAiModelCommandHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(EnableAiModelCommand command, CancellationToken ct)
    {
        var aiModel = await _aiModelRepository.GetByIdAsync(command.ModelId);
        if (aiModel.IsEnabled == true)
        {
            aiModel.SetEnabled(false);
        }
        else
        {
            aiModel.SetEnabled(true);
        }

        await _aiModelRepository.UpdateAsync(aiModel);
        return Result.Success(aiModel.IsEnabled);
    }
}