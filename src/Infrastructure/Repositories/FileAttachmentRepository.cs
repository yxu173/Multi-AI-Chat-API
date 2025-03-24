using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class FileAttachmentRepository : IFileAttachmentRepository
{
    private readonly ApplicationDbContext _context;

    public FileAttachmentRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<FileAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachment>> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .AsNoTracking()
            .Where(f => f.MessageId == messageId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachment>> GetByChatSessionIdAsync(Guid chatSessionId, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .AsNoTracking()
            .Join(_context.Messages,
                fa => fa.MessageId,
                m => m.Id,
                (fa, m) => new { FileAttachment = fa, Message = m })
            .Where(j => j.Message.ChatSessionId == chatSessionId)
            .Select(j => j.FileAttachment)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(FileAttachment fileAttachment, CancellationToken cancellationToken = default)
    {
        await _context.FileAttachments.AddAsync(fileAttachment, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fileAttachment = await _context.FileAttachments.FindAsync(new object[] { id }, cancellationToken);
        if (fileAttachment != null)
        {
            _context.FileAttachments.Remove(fileAttachment);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}