using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IFileAttachmentRepository
{
    Task AddAsync(FileAttachment fileAttachment);
}