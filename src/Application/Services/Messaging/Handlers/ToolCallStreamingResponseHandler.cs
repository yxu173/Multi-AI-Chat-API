using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;

namespace Application.Services.Messaging.Handlers;

public sealed class ToolCallStreamingResponseHandler : BaseStreamingResponseHandler
{
    public ToolCallStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler)
        : base(aiRequestHandler, streamProcessor, toolCallHandler) { }

    public override ResponseType ResponseType => ResponseType.ToolCall;
}
