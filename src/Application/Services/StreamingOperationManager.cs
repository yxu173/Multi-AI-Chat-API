using System;
using System.Collections.Concurrent;

namespace Application.Services;

public class StreamingOperationManager
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _streamingOperations = new();

    public CancellationToken RegisterStreaming(Guid messageId)
    {
        var cts = new CancellationTokenSource();
        if (!_streamingOperations.TryAdd(messageId, cts))
        {
            cts.Dispose(); // Clean up the unused CTS
            throw new InvalidOperationException($"A streaming operation is already registered for message ID {messageId}.");
        }
        return cts.Token;
    }

    public void StopStreaming(Guid messageId)
    {
        if (_streamingOperations.TryRemove(messageId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        // Optionally log if no operation is found: Console.WriteLine($"No operation found for {messageId}");
    }

    public void UnregisterStreaming(Guid messageId)
    {
        if (_streamingOperations.TryRemove(messageId, out var cts))
        {
            cts.Dispose();
        }
        // Optionally log if no operation is found: Console.WriteLine($"No operation found for {messageId}");
    }
}