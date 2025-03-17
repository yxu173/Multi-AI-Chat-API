using Application.Abstractions.Messaging;
using Application.Features.AiProviders.GetAllAiProviders;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiProviders.GetAiProviderById;

public sealed class GetAiProviderByIdQueryHandler : IQueryHandler<GetAiProviderByIdQuery, AiProviderDto>
{
    private readonly IAiProviderRepository _aiProviderRepository;

    public GetAiProviderByIdQueryHandler(IAiProviderRepository aiProviderRepository)
    {
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<AiProviderDto>> Handle(GetAiProviderByIdQuery request, CancellationToken cancellationToken)
    {
        var aiProvider = await _aiProviderRepository.GetByIdAsync(request.Id);

        var dto = new AiProviderDto(aiProvider.Id, aiProvider.Name, aiProvider.Description);
        return Result.Success(dto);
    }
}