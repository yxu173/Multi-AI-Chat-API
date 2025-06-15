using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Application.Services.Infrastructure
{
    public class StreamingOperationManager
    {
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _operations = new();
        private readonly ILogger<StreamingOperationManager> _logger;
        private readonly Timer _cleanupTimer;
        private readonly object _cleanupLock = new();

        public StreamingOperationManager(ILogger<StreamingOperationManager> logger)
        {
            _logger = logger;
            _cleanupTimer = new Timer(CleanupExpiredOperations, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public void RegisterOperation(Guid messageId, CancellationTokenSource cts)
        {
            if (_operations.TryRemove(messageId, out var existingCts))
            {
                existingCts.Cancel();
                existingCts.Dispose();
                _logger.LogDebug("Replaced existing streaming operation for message {MessageId}", messageId);
            }

            _operations[messageId] = cts;
            _logger.LogDebug("Registered streaming operation for message {MessageId}. Total active operations: {Count}", 
                messageId, _operations.Count);
        }

        public bool StopStreaming(Guid messageId)
        {
            if (_operations.TryGetValue(messageId, out var cts))
            {
                cts.Cancel();
                _operations.TryRemove(messageId, out _);
                _logger.LogDebug("Stopped streaming operation for message {MessageId}. Remaining operations: {Count}", 
                    messageId, _operations.Count);
                return true;
            }

            _logger.LogDebug("No active streaming operation found for message {MessageId}", messageId);
            return false;
        }

        public bool IsStreamingActive(Guid messageId)
        {
            return _operations.ContainsKey(messageId);
        }

        public int GetActiveOperationCount()
        {
            return _operations.Count;
        }

        public void CleanupAllOperations()
        {
            var operationsToCancel = _operations.ToList();
            foreach (var kvp in operationsToCancel)
            {
                try
                {
                    kvp.Value.Cancel();
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling streaming operation for message {MessageId}", kvp.Key);
                }
            }
            
            _operations.Clear();
            _logger.LogInformation("Cleaned up {Count} streaming operations", operationsToCancel.Count);
        }

        private void CleanupExpiredOperations(object? state)
        {
            lock (_cleanupLock)
            {
                var currentTime = DateTime.UtcNow;
                var operationsToRemove = new List<Guid>();

                foreach (var kvp in _operations)
                {
                    // Check if operation has been running for more than 30 minutes
                    // This is a simple cleanup mechanism - in production you might want
                    // to track operation start times more explicitly
                    if (kvp.Value.Token.IsCancellationRequested)
                    {
                        operationsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var messageId in operationsToRemove)
                {
                    if (_operations.TryRemove(messageId, out var cts))
                    {
                        try
                        {
                            cts.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing cancelled streaming operation for message {MessageId}", messageId);
                        }
                    }
                }

                if (operationsToRemove.Count > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} expired streaming operations. Remaining: {Remaining}", 
                        operationsToRemove.Count, _operations.Count);
                }
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            CleanupAllOperations();
        }
    }
}