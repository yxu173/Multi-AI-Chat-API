using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;

namespace Application.Services.AI.Interfaces;

public interface IAiRequestBuilder
{
    Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<PluginDefinition>? tools = null,
        CancellationToken cancellationToken = default);
}
