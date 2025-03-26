using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;

namespace Application.Features.AiModels.Queries.GetEnabledAiModels;

public sealed record GetEnabledAiModelsQuery() : IQuery<IReadOnlyList<AiModelDto>>;