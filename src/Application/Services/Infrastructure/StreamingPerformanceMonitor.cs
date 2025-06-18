using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Application.Services.Infrastructure
{
    public class StreamingPerformanceMonitor
    {
        private readonly ILogger<StreamingPerformanceMonitor> _logger;
        private readonly ConcurrentDictionary<Guid, StreamingMetrics> _activeStreams = new();
        private readonly Timer _metricsTimer;
        private readonly Timer _cleanupTimer;
        private readonly object _metricsLock = new();
        private readonly object _cleanupLock = new();

        public StreamingPerformanceMonitor(ILogger<StreamingPerformanceMonitor> logger)
        {
            _logger = logger;
            _metricsTimer = new Timer(LogPerformanceMetrics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            _cleanupTimer = new Timer(CleanupStaleStreams, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        }

        public void StartMonitoring(Guid messageId)
        {
            var metrics = new StreamingMetrics
            {
                MessageId = messageId,
                StartTime = DateTime.UtcNow,
                StartMemory = GC.GetTotalMemory(false)
            };

            _activeStreams[messageId] = metrics;
            _logger.LogDebug("Started performance monitoring for message {MessageId}", messageId);
        }

        public void RecordChunkProcessed(Guid messageId, int chunkSize, TimeSpan processingTime)
        {
            if (_activeStreams.TryGetValue(messageId, out var metrics))
            {
                lock (metrics)
                {
                    metrics.ChunksProcessed++;
                    metrics.TotalChunkSize += chunkSize;
                    metrics.TotalProcessingTime += processingTime;
                    metrics.LastChunkTime = DateTime.UtcNow;
                }
            }
        }

        public void RecordNotificationSent(Guid messageId, int notificationCount)
        {
            if (_activeStreams.TryGetValue(messageId, out var metrics))
            {
                lock (metrics)
                {
                    metrics.NotificationsSent += notificationCount;
                }
            }
        }

        public void StopMonitoring(Guid messageId)
        {
            if (_activeStreams.TryRemove(messageId, out var metrics))
            {
                var endTime = DateTime.UtcNow;
                var endMemory = GC.GetTotalMemory(false);
                var duration = endTime - metrics.StartTime;
                var memoryUsed = endMemory - metrics.StartMemory;

                lock (metrics)
                {
                    metrics.EndTime = endTime;
                    metrics.EndMemory = endMemory;
                    metrics.Duration = duration;
                    metrics.MemoryUsed = memoryUsed;
                }

                LogStreamCompletion(metrics);
            }
        }

        private void LogStreamCompletion(StreamingMetrics metrics)
        {
            var avgChunkSize = metrics.ChunksProcessed > 0 ? metrics.TotalChunkSize / metrics.ChunksProcessed : 0;
            var avgProcessingTime = metrics.ChunksProcessed > 0 ? metrics.TotalProcessingTime.TotalMilliseconds / metrics.ChunksProcessed : 0;
            var chunksPerSecond = metrics.Duration.TotalSeconds > 0 ? metrics.ChunksProcessed / metrics.Duration.TotalSeconds : 0;

            _logger.LogInformation(
                "Streaming completed for message {MessageId}: Duration={Duration:F2}s, " +
                "Chunks={ChunksProcessed}, AvgChunkSize={AvgChunkSize}bytes, " +
                "AvgProcessingTime={AvgProcessingTime:F2}ms, ChunksPerSecond={ChunksPerSecond:F2}, " +
                "MemoryUsed={MemoryUsed}bytes, Notifications={NotificationsSent}",
                metrics.MessageId,
                metrics.Duration.TotalSeconds,
                metrics.ChunksProcessed,
                avgChunkSize,
                avgProcessingTime,
                chunksPerSecond,
                metrics.MemoryUsed,
                metrics.NotificationsSent);
        }

        private void LogPerformanceMetrics(object? state)
        {
            lock (_metricsLock)
            {
                var activeCount = _activeStreams.Count;
                var totalMemory = GC.GetTotalMemory(false);
                var gcInfo = GC.GetGCMemoryInfo();
                
                // Get process memory information
                var process = Process.GetCurrentProcess();
                var processMemory = process.WorkingSet64;
                var totalPhysicalMemory = GetTotalPhysicalMemory();
                
                // Calculate memory pressure as percentage of physical memory
                var memoryPressurePercentage = totalPhysicalMemory > 0 
                    ? (double)processMemory / totalPhysicalMemory * 100.0 
                    : 0.0;

                // Get generation-specific memory info
                var gen0Size = GC.GetTotalMemory(false);
                var gen1Size = gcInfo.GenerationInfo[1].SizeAfterBytes;
                var gen2Size = gcInfo.GenerationInfo[2].SizeAfterBytes;

                _logger.LogInformation(
                    "Streaming Performance Summary: ActiveStreams={ActiveCount}, " +
                    "TotalMemory={TotalMemory}bytes ({TotalMemoryMB:F1}MB), " +
                    "ProcessMemory={ProcessMemory}bytes ({ProcessMemoryMB:F1}MB), " +
                    "GCHeapSize={HeapSize}bytes ({HeapSizeMB:F1}MB), " +
                    "MemoryPressure={MemoryPressure:F1}%, " +
                    "Gen0={Gen0Size}bytes, Gen1={Gen1Size}bytes, Gen2={Gen2Size}bytes, " +
                    "TotalPhysical={TotalPhysical}bytes ({TotalPhysicalGB:F1}GB)",
                    activeCount,
                    totalMemory,
                    totalMemory / 1024.0 / 1024.0,
                    processMemory,
                    processMemory / 1024.0 / 1024.0,
                    gcInfo.HeapSizeBytes,
                    gcInfo.HeapSizeBytes / 1024.0 / 1024.0,
                    memoryPressurePercentage,
                    gen0Size,
                    gen1Size,
                    gen2Size,
                    totalPhysicalMemory,
                    totalPhysicalMemory / 1024.0 / 1024.0 / 1024.0);

                // Memory pressure warnings
                if (memoryPressurePercentage > 80.0)
                {
                    _logger.LogWarning(
                        "High memory pressure detected: {MemoryPressure:F1}%. " +
                        "Consider reducing concurrent streams or enabling memory cleanup.",
                        memoryPressurePercentage);
                }

                // Log individual stream metrics for long-running streams
                var longRunningStreams = _activeStreams.Values
                    .Where(m => m.StartTime < DateTime.UtcNow.AddMinutes(-5))
                    .ToList();

                foreach (var stream in longRunningStreams)
                {
                    var runningTime = DateTime.UtcNow - stream.StartTime;
                    _logger.LogWarning(
                        "Long-running stream detected: MessageId={MessageId}, RunningTime={RunningTime:F2}s, " +
                        "ChunksProcessed={ChunksProcessed}",
                        stream.MessageId,
                        runningTime.TotalSeconds,
                        stream.ChunksProcessed);
                }

                // Force garbage collection if memory pressure is very high
                if (memoryPressurePercentage > 90.0)
                {
                    _logger.LogWarning("Very high memory pressure ({MemoryPressure:F1}%). Forcing garbage collection.", memoryPressurePercentage);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }
        }

        private long GetTotalPhysicalMemory()
        {
            try
            {
                // Try to get total physical memory using GC.GetGCMemoryInfo
                var gcInfo = GC.GetGCMemoryInfo();
                return gcInfo.TotalAvailableMemoryBytes;
            }
            catch
            {
                // Fallback: return a reasonable default (8GB)
                return 8L * 1024 * 1024 * 1024;
            }
        }

        private void CleanupStaleStreams(object? state)
        {
            lock (_cleanupLock)
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-10); // Clean up streams older than 10 minutes
                var staleStreams = _activeStreams.Values
                    .Where(m => m.StartTime < cutoffTime)
                    .ToList();

                foreach (var stream in staleStreams)
                {
                    if (_activeStreams.TryRemove(stream.MessageId, out var removedStream))
                    {
                        _logger.LogWarning(
                            "Cleaned up stale streaming session: MessageId={MessageId}, " +
                            "RunningTime={RunningTime:F2}s, ChunksProcessed={ChunksProcessed}",
                            stream.MessageId,
                            (DateTime.UtcNow - stream.StartTime).TotalSeconds,
                            stream.ChunksProcessed);
                    }
                }

                if (staleStreams.Any())
                {
                    _logger.LogInformation("Cleaned up {StaleCount} stale streaming sessions", staleStreams.Count);
                }
            }
        }

        public void Dispose()
        {
            _metricsTimer?.Dispose();
            _cleanupTimer?.Dispose();
        }

        private class StreamingMetrics
        {
            public Guid MessageId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public long StartMemory { get; set; }
            public long EndMemory { get; set; }
            public TimeSpan Duration { get; set; }
            public long MemoryUsed { get; set; }
            public int ChunksProcessed { get; set; }
            public long TotalChunkSize { get; set; }
            public TimeSpan TotalProcessingTime { get; set; }
            public DateTime LastChunkTime { get; set; }
            public int NotificationsSent { get; set; }
        }
    }
} 