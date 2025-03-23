using Application.Abstractions.Interfaces;
using Domain.Repositories;

namespace Application.Services;

public class PluginService
{
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;

    public PluginService(IChatSessionPluginRepository chatSessionPluginRepository,
        IPluginExecutorFactory pluginExecutorFactory)
    {
        _chatSessionPluginRepository = chatSessionPluginRepository ??
                                       throw new ArgumentNullException(nameof(chatSessionPluginRepository));
        _pluginExecutorFactory =
            pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
    }

    public async Task<string> ExecutePluginsAsync(Guid chatSessionId, string content,
        CancellationToken cancellationToken = default)
    {
        var plugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatSessionId, cancellationToken);
        var applicablePlugins = plugins
            .Where(p => p.IsActive)
            .Select(p => new { Plugin = _pluginExecutorFactory.GetPlugin(p.PluginId), Order = p.Order })
            .Where(p => p.Plugin.CanHandle(content))
            .OrderBy(p => p.Order)
            .ToList();

        if (!applicablePlugins.Any())
            return content;

        var pluginGroups = applicablePlugins.GroupBy(p => p.Order).OrderBy(g => g.Key).ToList();
        var currentContent = content;

        foreach (var group in pluginGroups)
        {
            var pluginTasks = group.Select(p => p.Plugin.ExecuteAsync(currentContent, cancellationToken));
            var results = await Task.WhenAll(pluginTasks);
            var successfulResults = results.Where(r => r.Success).Select(r => r.Result).ToList();

            if (successfulResults.Any())
            {
                currentContent =
                    $"{currentContent}\n\n**Plugin Results (Order {group.Key}):**\n{string.Join("\n", successfulResults)}";
            }
        }

        return currentContent;
    }
}