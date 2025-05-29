using Application.Abstractions.Messaging;

namespace Application.Features.UserAiModelSettings.ResetSystemInstructions;

public sealed record ResetSystemInstructionsCommand(Guid UserId) : ICommand<bool>;