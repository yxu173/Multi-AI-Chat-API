using Application.Services.AI;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.Messaging.Handlers;

public sealed class ImageResponseHandler : BaseStreamingResponseHandler
{
    public ImageResponseHandler(ConversationTurnProcessor turnProcessor)
        : base(turnProcessor) { }

    public override ResponseType ResponseType => ResponseType.Image;

    protected override bool AllowToolCalls => false; // Image responses don't support tool calls
}
