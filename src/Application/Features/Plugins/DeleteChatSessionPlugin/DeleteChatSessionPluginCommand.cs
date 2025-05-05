using FastEndpoints;
using SharedKernel;

namespace Application.Features.Plugins.DeleteChatSessionPlugin;

public sealed record DeleteChatSessionPluginCommand(Guid Id) : ICommand<Result<bool>>;