using FastEndpoints;

namespace Application.Notifications;

public record DeepSearchChunkReceivedNotification(Guid ChatSessionId, string Chunk) : IEvent; 