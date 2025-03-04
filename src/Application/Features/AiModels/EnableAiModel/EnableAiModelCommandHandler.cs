using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.EnableAiModel;

public sealed class EnableAiModelCommandHandler : ICommandHandler<EnableAiModelCommand, bool>
{
    private readonly IAiModelRepository _aiModelRepository;

    public EnableAiModelCommandHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<bool>> Handle(EnableAiModelCommand request, CancellationToken cancellationToken)
    {
        var aiModel = await _aiModelRepository.GetByIdAsync(request.ModelId);
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