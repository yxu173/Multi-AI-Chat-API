using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.GetAllAiModels;

public record GetAllAiModelsQuery() : IQuery<IReadOnlyList<AiModelDto>>;