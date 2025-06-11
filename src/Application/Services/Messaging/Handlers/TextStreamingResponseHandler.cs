using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Messaging.Handlers;

public sealed class TextStreamingResponseHandler : BaseStreamingResponseHandler
{
    public TextStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        ToolCallHandler toolCallHandler,
        ILogger<BaseStreamingResponseHandler> logger)
        : base(aiRequestHandler, toolCallHandler, logger) { }

    public override ResponseType ResponseType => ResponseType.Text;

    protected override bool AllowToolCalls => false;
}
