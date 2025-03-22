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
            cts.Dispose();
            throw new InvalidOperationException(
                $"A streaming operation is already registered for message ID {messageId}.");
        }

        return cts.Token;
    }

    public void StopStreaming(Guid operationId)
    {
        if (_streamingOperations.TryRemove(operationId, out var cts))
        {
            Console.WriteLine($"Cancelling operation {operationId}");
            cts.Cancel();
            cts.Dispose();
        }
        else
        {
            Console.WriteLine($"No operation found for {operationId}");
        }
    }

    public void UnregisterStreaming(Guid messageId)
    {
        if (_streamingOperations.TryRemove(messageId, out var cts))
        {
            cts.Dispose();
        }

        Console.WriteLine($"No operation found for {messageId}");
    }
}