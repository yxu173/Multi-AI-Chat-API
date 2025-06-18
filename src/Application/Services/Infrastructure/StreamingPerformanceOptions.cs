namespace Application.Services.Infrastructure
{
    public class StreamingPerformanceOptions
    {
        public const string SectionName = "StreamingPerformance";

        /// <summary>
        /// Maximum number of chunks to batch before sending notifications
        /// </summary>
        public int MaxChunkBatchSize { get; set; } = 10;

        /// <summary>
        /// Maximum delay in milliseconds before flushing notification batches
        /// </summary>
        public int NotificationBatchDelayMs { get; set; } = 50;

        /// <summary>
        /// Maximum size of StringBuilder pool
        /// </summary>
        public int StringBuilderPoolSize { get; set; } = 100;

        /// <summary>
        /// Maximum size of MessageDto list pool
        /// </summary>
        public int MessageListPoolSize { get; set; } = 50;

        /// <summary>
        /// Maximum size of ToolCallState dictionary pool
        /// </summary>
        public int ToolCallStatePoolSize { get; set; } = 50;

        /// <summary>
        /// Enable performance monitoring
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// Enable memory pressure monitoring
        /// </summary>
        public bool EnableMemoryPressureMonitoring { get; set; } = true;

        /// <summary>
        /// Memory pressure threshold (percentage) to trigger cleanup
        /// </summary>
        public double MemoryPressureThreshold { get; set; } = 80.0;

        /// <summary>
        /// Maximum number of concurrent streaming operations
        /// </summary>
        public int MaxConcurrentStreams { get; set; } = 1000;

        /// <summary>
        /// Timeout for streaming operations in minutes
        /// </summary>
        public int StreamingTimeoutMinutes { get; set; } = 30;

        /// <summary>
        /// Enable JSON parsing optimization with Utf8JsonReader
        /// </summary>
        public bool EnableOptimizedJsonParsing { get; set; } = true;

        /// <summary>
        /// Enable object pooling for better memory management
        /// </summary>
        public bool EnableObjectPooling { get; set; } = true;

        /// <summary>
        /// Enable notification batching
        /// </summary>
        public bool EnableNotificationBatching { get; set; } = true;

        /// <summary>
        /// Log level for performance metrics
        /// </summary>
        public string PerformanceLogLevel { get; set; } = "Information";
    }
} 