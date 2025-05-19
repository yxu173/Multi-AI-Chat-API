using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiProviders.GetAllAiProviders;

public sealed class GetAllAiProvidersQueryHandler : IQueryHandler<GetAllAiProvidersQuery, IReadOnlyList<AiProviderDto>>
{
    private readonly IAiProviderRepository _aiProviderRepository;

    public GetAllAiProvidersQueryHandler(IAiProviderRepository aiProviderRepository)
    {
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<IReadOnlyList<AiProviderDto>>> ExecuteAsync(GetAllAiProvidersQuery command, CancellationToken ct)
    {
        var aiProviders = await _aiProviderRepository.GetAllAsync();
        var dtos = aiProviders.Select(p => new AiProviderDto(p.Id, p.Name, p.Description)).ToList();
        return Result.Success<IReadOnlyList<AiProviderDto>>(dtos);
    }
}