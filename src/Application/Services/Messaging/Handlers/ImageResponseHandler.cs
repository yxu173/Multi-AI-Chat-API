using Application.Services.AI.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;

namespace Application.Services.Messaging.Handlers;

public sealed class ImageResponseHandler : BaseStreamingResponseHandler
{
    public ImageResponseHandler(
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler)
        : base(aiRequestHandler, streamProcessor, toolCallHandler) { }

    public override ResponseType ResponseType => ResponseType.Image;

    protected override bool AllowToolCalls => false; // Image responses don't support tool calls
}
