using Application.Abstractions.Messaging;
using Application.Features.AiModels.GetAllAiModels;

namespace Application.Features.AiModels.GetEnabledAiModels;

public sealed record GetEnabledAiModelsQuery() : IQuery<IReadOnlyList<AiModelDto>>;