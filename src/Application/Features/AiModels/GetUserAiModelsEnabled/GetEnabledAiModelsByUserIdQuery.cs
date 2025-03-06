using Application.Abstractions.Messaging;
using Application.Features.AiModels.GetAllAiModels;

namespace Application.Features.AiModels.GetUserAiModelsEnabled;

public sealed record GetEnabledAiModelsByUserIdQuery(Guid UserId) : IQuery<IReadOnlyList<AiModelDto>>;