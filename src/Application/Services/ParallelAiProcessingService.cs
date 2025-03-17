using System.Collections.Concurrent;
using System.Text;
using Application.Abstractions.Interfaces;

namespace Application.Services;

public class ParallelAiProcessingService
{
    private readonly IAiModelServiceFactory _aiModelServiceFactory;

    public ParallelAiProcessingService(IAiModelServiceFactory aiModelServiceFactory)
    {
        _aiModelServiceFactory = aiModelServiceFactory;
    }

    public async Task<IEnumerable<ParallelAiResponse>> ProcessInParallelAsync(
        Guid userId,
        IEnumerable<MessageDto> messages,
        IEnumerable<Guid> modelIds,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<ParallelAiResponse>>();

        foreach (var modelId in modelIds)
        {
            tasks.Add(ProcessWithModelAsync(userId, messages, modelId, cancellationToken));
        }

        return await Task.WhenAll(tasks);
    }

    private async Task<ParallelAiResponse> ProcessWithModelAsync(
        Guid userId,
        IEnumerable<MessageDto> messages,
        Guid modelId,
        CancellationToken cancellationToken)
    {
        try
        {
            var aiService = _aiModelServiceFactory.GetService(userId, modelId);
            var content = new StringBuilder();
            int inputTokens = 0;
            int outputTokens = 0;

            await foreach (var chunk in aiService.StreamResponseAsync(messages))
            {
                content.Append(chunk.Content);
                inputTokens = chunk.InputTokens;
                outputTokens = chunk.OutputTokens;

                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            return new ParallelAiResponse(
                modelId,
                content.ToString(),
                true,
                inputTokens,
                outputTokens,
                null
            );
        }
        catch (Exception ex)
        {
            return new ParallelAiResponse(
                modelId,
                $"Error: {ex.Message}",
                false,
                0,
                0,
                ex.Message
            );
        }
    }
}

public record ParallelAiResponse(
    Guid ModelId,
    string Content,
    bool Success,
    int InputTokens,
    int OutputTokens,
    string? ErrorMessage
);