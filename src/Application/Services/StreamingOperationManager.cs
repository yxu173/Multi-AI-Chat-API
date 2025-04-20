using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Application.Services
{
  public class StreamingOperationManager
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _operations = new();

    public void RegisterOperation(Guid messageId, CancellationTokenSource cts)
    {
        if (_operations.TryRemove(messageId, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }
        _operations[messageId] = cts;
    }

   public bool StopStreaming(Guid messageId)
    {
        if (_operations.TryGetValue(messageId, out var cts))
        {
            cts.Cancel();
            _operations.TryRemove(messageId, out _);
            return true;
        }
        return false;
    }
    
    public bool IsStreamingActive(Guid messageId)
    {
        return _operations.ContainsKey(messageId);
    }
}
}