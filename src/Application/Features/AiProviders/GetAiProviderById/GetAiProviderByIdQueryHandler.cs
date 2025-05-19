using Application.Abstractions.Messaging;
using Application.Features.AiProviders.GetAllAiProviders;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiProviders.GetAiProviderById;

public sealed class GetAiProviderByIdQueryHandler : IQueryHandler<GetAiProviderByIdQuery, AiProviderDto>
{
    private readonly IAiProviderRepository _aiProviderRepository;

    public GetAiProviderByIdQueryHandler(IAiProviderRepository aiProviderRepository)
    {
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<AiProviderDto>> ExecuteAsync(GetAiProviderByIdQuery command, CancellationToken ct)
    {
        var aiProvider = await _aiProviderRepository.GetByIdAsync(command.Id);

        var dto = new AiProviderDto(aiProvider.Id, aiProvider.Name, aiProvider.Description);
        return Result.Success(dto);
    }
}