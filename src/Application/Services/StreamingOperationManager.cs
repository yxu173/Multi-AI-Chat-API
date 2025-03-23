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
            _operations[messageId] = cts;
        }

        public void StopStreaming(Guid messageId)
        {
            if (_operations.TryRemove(messageId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}