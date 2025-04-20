using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IFileAttachmentRepository
{
    Task<FileAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileAttachment>> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileAttachment>> GetByChatSessionIdAsync(Guid chatSessionId, CancellationToken cancellationToken = default);
    Task AddAsync(FileAttachment fileAttachment, CancellationToken cancellationToken = default);
    Task UpdateAsync(FileAttachment fileAttachment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}