namespace Application.Features.AiModels.DTOs;

public sealed record AiModelDto(
    Guid ModelId,
    string Name,
    bool IsEnabled,
    bool SupportsVision,
    int? ContextLength,
    bool PluginsSupported
);