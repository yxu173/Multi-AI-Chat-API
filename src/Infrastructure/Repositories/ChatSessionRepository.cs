using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ChatSessionRepository : IChatSessionRepository
{
    private readonly ApplicationDbContext _context;

    public ChatSessionRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ChatSession> GetByIdAsync(Guid id)
    {
        return await _context.ChatSessions
            .Include(cs => cs.Messages)
            .ThenInclude(m => m.FileAttachments)
            .FirstOrDefaultAsync(cs => cs.Id == id);
    }

    public async Task AddAsync(ChatSession chatSession)
    {
        await _context.ChatSessions.AddAsync(chatSession);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(ChatSession chatSession)
    {
        _context.ChatSessions.Update(chatSession);
        await _context.SaveChangesAsync();
    }
}