using Application.Services;

namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    /// <summary>
    /// Streams the AI model response asynchronously
    /// </summary>
    /// <param name="history">Chat message history</param>
    /// <returns>Stream of response chunks</returns>
    IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history);
    
    /// <summary>
    /// Stops the currently streaming response
    /// </summary>
    void StopResponse();
}

public record StreamResponse(string Content, int InputTokens, int OutputTokens);