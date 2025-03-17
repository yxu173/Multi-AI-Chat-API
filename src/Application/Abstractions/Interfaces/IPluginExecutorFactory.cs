namespace Application.Abstractions.Interfaces;

public interface IPluginExecutorFactory
{
    IChatPlugin GetPlugin(Guid pluginId);
}