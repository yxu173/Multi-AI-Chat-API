using Application.Abstractions.Messaging;
using Application.Features.AiModels.GetAllAiModels;

namespace Application.Features.AiModels.GetUserAiModels;

public sealed record GetUserAiModelsQuery(Guid UserId) : IQuery<IReadOnlyList<AiModelDto>>;