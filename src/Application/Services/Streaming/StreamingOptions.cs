namespace Application.Services.Streaming;

public class StreamingOptions
{
    public const string SectionName = "Streaming";

    public int MaxRetries { get; set; } = 3;
    public double InitialRetryDelaySeconds { get; set; } = 2.0;
    public double RetryBackoffFactor { get; set; } = 2.0;
    public int MaxConversationTurns { get; set; } = 5;
    
    // Notification Batching
    public bool EnableNotificationBatching { get; set; } = true;
    public int MaxChunkBatchSize { get; set; } = 10;
    public int NotificationBatchDelayMs { get; set; } = 50;

    // Performance & Memory
    public bool EnablePerformanceMonitoring { get; set; } = true;
    public bool EnableMemoryPressureMonitoring { get; set; } = true;
    public bool EnableObjectPooling { get; set; } = true;
    public int StringBuilderPoolSize { get; set; } = 100;
    public int MessageListPoolSize { get; set; } = 50;
    public int ToolCallStatePoolSize { get; set; } = 50;
} 