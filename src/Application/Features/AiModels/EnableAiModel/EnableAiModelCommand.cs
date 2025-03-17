using System.Diagnostics.SymbolStore;
using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.EnableAiModel;

public sealed record EnableAiModelCommand(Guid ModelId) : ICommand<bool>;