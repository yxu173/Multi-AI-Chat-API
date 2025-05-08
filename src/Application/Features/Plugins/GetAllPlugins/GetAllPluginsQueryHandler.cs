using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Plugins.GetAllPlugins;

public class GetAllPluginsQueryHandler : IQueryHandler<GetAllPluginsQuery, IReadOnlyList<PluginResponse>>
{
    private readonly IPluginRepository _pluginRepository;

    public GetAllPluginsQueryHandler(IPluginRepository pluginRepository)
    {
        _pluginRepository = pluginRepository;
    }

    public async Task<Result<IReadOnlyList<PluginResponse>>> ExecuteAsync(GetAllPluginsQuery command, CancellationToken ct)
    {
        var plugins = await _pluginRepository.GetAllAsync();

        var responses = plugins.Select(p => new PluginResponse(
            p.Id,
            p.Name,
            p.Description,
            p.IconUrl
        )).ToList();
        return Result.Success<IReadOnlyList<PluginResponse>>(responses);
    }

}