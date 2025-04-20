using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiProviders.DeleteAiProvider;

public sealed class DeleteAiProviderCommandHandler : ICommandHandler<DeleteAiProviderCommand, bool>
{
    private readonly IAiProviderRepository _aiProviderRepository;

    public DeleteAiProviderCommandHandler(IAiProviderRepository aiProviderRepository)
    {
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<bool>> Handle(DeleteAiProviderCommand request, CancellationToken cancellationToken)
    {
        var result = await _aiProviderRepository.DeleteAsync(request.Id);
        return Result.Success(result);
    }
}