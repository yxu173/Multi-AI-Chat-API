namespace Application.Abstractions.Interfaces;

public interface IAiModelService
{
    Task<string> GetResponseAsync(string modelType, string message);
    IAsyncEnumerable<string> GetStreamingResponseAsync(string modelType, string message);
}