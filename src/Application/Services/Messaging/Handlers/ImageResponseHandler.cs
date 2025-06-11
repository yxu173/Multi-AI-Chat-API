using Application.Services.AI.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;
using Microsoft.Extensions.Logging;

namespace Application.Services.Messaging.Handlers;

public sealed class ImageResponseHandler : BaseStreamingResponseHandler
{
    public ImageResponseHandler(
        IAiRequestHandler aiRequestHandler,
        ToolCallHandler toolCallHandler,
        ILogger<BaseStreamingResponseHandler> logger)
        : base(aiRequestHandler, toolCallHandler, logger) { }

    public override ResponseType ResponseType => ResponseType.Image;

    protected override bool AllowToolCalls => false; // Image responses don't support tool calls
}
