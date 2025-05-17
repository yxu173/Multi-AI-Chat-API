namespace Web.Api.Contracts.Plugins;

public class TogglePluginRequest
{
    public Guid PluginId { get; set; }
    public bool IsEnabled { get; set; }
}
