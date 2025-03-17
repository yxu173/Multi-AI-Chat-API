using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;

namespace Infrastructure.Repositories;

public class FileAttachmentRepository : IFileAttachmentRepository
{
    private readonly ApplicationDbContext _context;

    public FileAttachmentRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(FileAttachment fileAttachment)
    {
        await _context.FileAttachments.AddAsync(fileAttachment);
        await _context.SaveChangesAsync();
    }
}