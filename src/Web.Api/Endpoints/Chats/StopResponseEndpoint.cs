using Application.Services.Infrastructure;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class StopResponseRequest
{
    public Guid MessageId { get; set; }
}

public class StopResponseEndpoint : Endpoint<StopResponseRequest>
{
    private readonly StreamingOperationManager _streamingOperationManager;
    public StopResponseEndpoint(StreamingOperationManager streamingOperationManager)
    {
        _streamingOperationManager = streamingOperationManager;
    }

    public override void Configure()
    {
        Post("/api/chat/StopResponse/{MessageId}");
        Description(x => x.Produces(200).Produces(404));
    }

    public override async Task HandleAsync(StopResponseRequest req, CancellationToken ct)
    {
        bool stopped = _streamingOperationManager.StopStreaming(req.MessageId);
        if (stopped)
        {
            await SendOkAsync(new { Message = "Streaming stopped successfully." }, ct);
        }
        else
        {
            await SendAsync(new { Message = "No active streaming operation found for the provided message ID." }, 404, ct);
        }
    }
} 