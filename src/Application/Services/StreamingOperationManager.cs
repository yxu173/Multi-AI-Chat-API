using System.Collections.Concurrent;

namespace Application.Services;

public class StreamingOperationManager
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeStreams = new();

    public CancellationTokenSource StartStreaming(Guid messageId)
    {
        var cts = new CancellationTokenSource();
        _activeStreams.TryAdd(messageId, cts);
        return cts;
    }

    public void StopStreaming(Guid messageId)
    {
        if (_activeStreams.TryRemove(messageId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void CompleteStreaming(Guid messageId)
    {
        if (_activeStreams.TryRemove(messageId, out var cts))
        {
            cts.Dispose();
        }
    }
}