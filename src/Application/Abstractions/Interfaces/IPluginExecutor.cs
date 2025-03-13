namespace Application.Abstractions.Interfaces;

public interface IPluginExecutor
{
    bool CanHandle(string userMessage);
    Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default);
    Guid PluginId { get; }
}