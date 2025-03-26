using Domain.Aggregates.Chats;
using Microsoft.AspNetCore.Http;

namespace Application.Abstractions.Interfaces;

public interface IFileService
{
    Task<FileAttachment> UploadFileAsync(Guid chatSessionId, IFormFile file, Guid userId,
        CancellationToken cancellationToken);
}