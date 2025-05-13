using Domain.Enums;

namespace Application.Abstractions.Interfaces;

public interface IAiModelServiceFactory
{
    IAiModelService GetService(Guid userId, Guid modelId, Guid? aiAgentId = null);
}