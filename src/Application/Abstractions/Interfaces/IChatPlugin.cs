using System.Text.Json.Nodes;

namespace Application.Abstractions.Interfaces;

public interface IChatPlugin
{
    string Name { get; }
    string Description { get; }
    JsonObject GetParametersSchema();
}

public interface IChatPlugin<T> : IChatPlugin
{
    Task<PluginResult<T>> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("This plugin does not implement ExecuteAsync.");
    }
}

public class PluginResult<T>
{
    public T Result { get; }
    public bool Success { get; }
    public string ErrorMessage { get; }

    public PluginResult(T result, bool success, string errorMessage = null)
    {
        Result = result;
        Success = success;
        ErrorMessage = errorMessage;
    }
}