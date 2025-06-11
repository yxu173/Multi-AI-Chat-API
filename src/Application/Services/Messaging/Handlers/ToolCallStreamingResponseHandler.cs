using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;
using Microsoft.Extensions.Logging;

namespace Application.Services.Messaging.Handlers;

public sealed class ToolCallStreamingResponseHandler : BaseStreamingResponseHandler
{
    public ToolCallStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        ToolCallHandler toolCallHandler,
        ILogger<BaseStreamingResponseHandler> logger)
        : base(aiRequestHandler, toolCallHandler, logger) { }

    public override ResponseType ResponseType => ResponseType.ToolCall;
}
