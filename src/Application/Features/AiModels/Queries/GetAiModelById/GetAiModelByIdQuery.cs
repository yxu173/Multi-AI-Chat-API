using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;

namespace Application.Features.AiModels.Queries.GetAiModelById;

public sealed record GetAiModelByIdQuery(Guid ModelId) : IQuery<DetailedAiModelDto>; 