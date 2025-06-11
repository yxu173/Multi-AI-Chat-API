using Application.Services.Messaging;
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.AI.Interfaces;

public record AiOrchestrationRequest(
    Guid ChatSessionId,
    Guid UserId,
    Guid AiMessageId,
    bool EnableThinking,
    string? ImageSize,
    int? NumImages,
    string? OutputFormat,
    bool? EnableSafetyChecker,
    string? SafetyTolerance
);

public interface IAiRequestOrchestrator
{
    Task ProcessRequestAsync(AiOrchestrationRequest request, CancellationToken cancellationToken);
} 