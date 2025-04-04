using System.Text.Json.Nodes;

namespace Application.Abstractions.Interfaces;

public interface IChatPlugin
{
    string Name { get; }
    string Description { get; }
    JsonObject GetParametersSchema();
    Task<PluginResult> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default);
}

public class PluginResult
{
    public string Result { get; }
    public bool Success { get; }
    public string ErrorMessage { get; }

    public PluginResult(string result, bool success, string errorMessage = null)
    {
        Result = result;
        Success = success;
        ErrorMessage = errorMessage;
    }
}