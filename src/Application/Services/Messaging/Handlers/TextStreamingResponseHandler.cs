using Application.Services.AI.Streaming;
using Application.Services.Utilities;

namespace Application.Services.Messaging.Handlers;

public sealed class TextStreamingResponseHandler : BaseStreamingResponseHandler
{
    public TextStreamingResponseHandler(ConversationTurnProcessor turnProcessor)
        : base(turnProcessor) { }

    public override ResponseType ResponseType => ResponseType.Text;

    protected override bool AllowToolCalls => false;
}
