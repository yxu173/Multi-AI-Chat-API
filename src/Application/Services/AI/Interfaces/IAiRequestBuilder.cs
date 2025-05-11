using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.AI.Interfaces;

public interface IAiRequestBuilder
{
    Task<AiRequestPayload> PreparePayloadAsync(
        AiRequestContext context,
        List<object>? tools = null,
        CancellationToken cancellationToken = default);
}
