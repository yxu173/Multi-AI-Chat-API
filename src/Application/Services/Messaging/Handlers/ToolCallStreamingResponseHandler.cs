using Application.Services.AI.Streaming;
using Application.Services.Utilities;

namespace Application.Services.Messaging.Handlers;

public sealed class ToolCallStreamingResponseHandler : BaseStreamingResponseHandler
{
    public ToolCallStreamingResponseHandler(ConversationTurnProcessor turnProcessor)
        : base(turnProcessor) { }

    public override ResponseType ResponseType => ResponseType.ToolCall;
}
