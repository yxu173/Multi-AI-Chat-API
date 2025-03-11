using Application.Abstractions.Interfaces;
using Domain.Repositories;

namespace Application.Services;

public class PluginManager
{
    private readonly IEnumerable<IChatPlugin> _plugins;
    private readonly IUserPluginPreferenceRepository _userPluginPreferenceRepository;

    public PluginManager(
        IEnumerable<IChatPlugin> plugins,
        IUserPluginPreferenceRepository userPluginPreferenceRepository)
    {
        _plugins = plugins;
        _userPluginPreferenceRepository = userPluginPreferenceRepository;
    }

    public IEnumerable<IChatPlugin> GetAllPlugins() => _plugins;

   
    public async Task<List<PluginInfo>> GetUserPluginsAsync(Guid userId)
    {
        var preferences = await _userPluginPreferenceRepository.GetAllByUserIdAsync(userId);
        var result = new List<PluginInfo>();

        foreach (var plugin in _plugins)
        {
            var preference = preferences.FirstOrDefault(p => p.PluginId == plugin.Id);
            bool isEnabled = preference?.IsEnabled ?? false;

            result.Add(new PluginInfo(
                plugin.Id,
                plugin.Name,
                plugin.Description,
                isEnabled
            ));
        }

        return result;
    }

 
    public async Task SetPluginEnabledAsync(Guid userId, string pluginId, bool isEnabled)
    {
        var preference = await _userPluginPreferenceRepository.GetByUserIdAndPluginIdAsync(userId, pluginId);
        
        if (preference == null)
        {
            preference = Domain.Aggregates.Users.UserPluginPreference.Create(userId, pluginId, isEnabled);
            await _userPluginPreferenceRepository.AddAsync(preference);
        }
        else
        {
            preference.SetEnabled(isEnabled);
            await _userPluginPreferenceRepository.UpdateAsync(preference);
        }
    }

  
    public async Task<IEnumerable<PluginResult>> ExecuteApplicablePluginsAsync(
        Guid userId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var preferences = await _userPluginPreferenceRepository.GetAllByUserIdAsync(userId);
        var enabledPluginIds = preferences.Where(p => p.IsEnabled).Select(p => p.PluginId).ToHashSet();
        
        var applicablePlugins = _plugins
            .Where(p => enabledPluginIds.Contains(p.Id) && p.CanHandle(userMessage))
            .ToList();
            
        var results = new List<PluginResult>();
        
        foreach (var plugin in applicablePlugins)
        {
            try
            {
                var result = await plugin.ExecuteAsync(userMessage, cancellationToken);
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(new PluginResult(
                    $"Error executing plugin {plugin.Name}: {ex.Message}",
                    false,
                    plugin.Name,
                    ex.Message
                ));
            }
        }
        
        return results;
    }
}

// Record to return plugin information with enabled status
public record PluginInfo(
    string Id,
    string Name,
    string Description,
    bool IsEnabled
);