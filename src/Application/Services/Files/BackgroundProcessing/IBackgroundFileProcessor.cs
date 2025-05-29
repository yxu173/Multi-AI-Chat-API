namespace Application.Services.Files.BackgroundProcessing;

public interface IBackgroundFileProcessor
{
    Task ProcessFileAttachmentAsync(Guid fileAttachmentId, CancellationToken cancellationToken = default);
} 