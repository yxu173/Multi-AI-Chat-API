using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;

namespace Application.Features.AiModels.Queries.GetUserAiModelsEnabled;

public sealed record GetEnabledAiModelsByUserIdQuery(Guid UserId) : IQuery<IReadOnlyList<AiModelDto>>;