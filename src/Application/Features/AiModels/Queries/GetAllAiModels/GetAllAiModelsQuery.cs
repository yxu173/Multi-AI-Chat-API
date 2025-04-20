using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;

namespace Application.Features.AiModels.Queries.GetAllAiModels;

public record GetAllAiModelsQuery() : IQuery<IReadOnlyList<AiModelDto>>;