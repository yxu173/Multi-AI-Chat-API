using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;

namespace Infrastructure.Services;

public class ChatGptService : IAiModelService
{
    public async IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        yield return "Hello from ChatGPT ";
        await Task.Delay(100);
        yield return "How can I assist you?";
    }
}