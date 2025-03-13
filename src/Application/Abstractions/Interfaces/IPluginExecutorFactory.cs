namespace Application.Abstractions.Interfaces;

public interface IPluginExecutorFactory
{
    IPluginExecutor GetExecutor(Guid pluginId);
}