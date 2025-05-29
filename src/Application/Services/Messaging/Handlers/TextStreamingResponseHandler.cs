using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;
using Domain.Enums;

namespace Application.Services.Messaging.Handlers;

public sealed class TextStreamingResponseHandler : BaseStreamingResponseHandler
{
    public TextStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler)
        : base(aiRequestHandler, streamProcessor, toolCallHandler) { }

    public override ResponseType ResponseType => ResponseType.Text;

    protected override bool AllowToolCalls => false;
}
