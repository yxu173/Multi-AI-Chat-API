using Domain.Enums;

namespace Application.Abstractions.Interfaces;

public interface IAiModelServiceFactory
{
    IAiModelService GetService(ModelType modelType);
}