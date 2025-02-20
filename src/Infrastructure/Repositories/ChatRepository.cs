using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ChatRepository: IChatRepository
{
    private readonly ApplicationDbContext _context;

    public ChatRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> CreateChatSessionAsync(ChatSession chatSession)
    {
        await _context.ChatSessions.AddAsync(chatSession);
        await _context.SaveChangesAsync();
        return chatSession.Id;
    }

    public async Task<ChatSession> GetChatSessionByIdAsync(Guid chatSessionId)
    {
        return await _context.ChatSessions
            .Include(cs => cs.Messages)
            .FirstOrDefaultAsync(cs => cs.Id == chatSessionId);
    }

    public async Task<IEnumerable<ChatSession>> GetChatSessionsByUserIdAsync(Guid userId)
    {
        return await _context.ChatSessions
            .Include(cs => cs.Messages)
            .Where(cs => cs.UserId == userId)
            .ToListAsync();
    }
}