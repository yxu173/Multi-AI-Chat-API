namespace Application.Features.AiModels.GetAllAiModels;

public sealed record AiModelDto(
    Guid ModelId,
    string Name,
    bool IsEnabled
);