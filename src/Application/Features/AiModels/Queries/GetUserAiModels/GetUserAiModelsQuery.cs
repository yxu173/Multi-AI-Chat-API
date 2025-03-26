using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;

namespace Application.Features.AiModels.Queries.GetUserAiModels;

public sealed record GetUserAiModelsQuery(Guid UserId) : IQuery<IReadOnlyList<AiModelDto>>;