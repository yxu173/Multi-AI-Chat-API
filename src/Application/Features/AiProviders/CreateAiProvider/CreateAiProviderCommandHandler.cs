using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiProviders.CreateAiProvider;

public sealed class CreateAiProviderCommandHandler : ICommandHandler<CreateAiProviderCommand, bool>
{
    private readonly IAiProviderRepository _providerRepository;

    public CreateAiProviderCommandHandler(IAiProviderRepository providerRepository)
    {
        _providerRepository = providerRepository;
    }

    public async Task<Result<bool>> Handle(CreateAiProviderCommand request, CancellationToken cancellationToken)
    {
        var aiProvider = AiProvider.Create(request.Name,request.Description);
        
        await _providerRepository.AddAsync(aiProvider);
        
        return Result.Success(true);
    }
}