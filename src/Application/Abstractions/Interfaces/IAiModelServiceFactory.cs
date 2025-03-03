using Domain.Enums;

namespace Application.Abstractions.Interfaces;

public interface IAiModelServiceFactory
{
    IAiModelService GetService(Guid modelId, string customApiKey = null);
}