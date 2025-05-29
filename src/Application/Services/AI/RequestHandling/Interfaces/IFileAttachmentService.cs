using Application.Services.AI.RequestHandling.Models;

namespace Application.Services.AI.RequestHandling.Interfaces;

public interface IFileAttachmentService
{
    Task<FileBase64Data?> GetBase64Async(Guid fileId, CancellationToken cancellationToken = default);
}
