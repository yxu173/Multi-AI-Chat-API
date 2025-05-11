using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.AI.Interfaces;

public interface IAiRequestHandler
{
    Task<AiRequestPayload> PrepareRequestPayloadAsync(
        AiRequestContext context,
        CancellationToken cancellationToken = default);
}
