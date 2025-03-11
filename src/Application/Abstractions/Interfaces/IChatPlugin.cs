namespace Application.Abstractions.Interfaces;

public interface IChatPlugin
{
    string Id { get; }
    string Name { get; }
    string Description { get; }

    bool CanHandle(string userMessage);

    Task<PluginResult> ExecuteAsync(string userMessage, CancellationToken cancellationToken = default);
}

public record PluginResult(
    string Result,
    bool Success,
    string PluginName,
    string ErrorMessage = null
);