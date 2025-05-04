namespace Application.Services.AI.PayloadBuilders;

public class ImagenInstance
{
    public string Prompt { get; set; } = string.Empty;
    public int? NumberOfImages { get; set; }
    public string? Size { get; set; }
}

public class ImagenPayload
{
    public ImagenInstance[] Instances { get; set; } = [];
}

public class ImagenPayloadBuilder : IPayloadBuilder
{
    public AiRequestPayload PreparePayload(AiRequestContext context, List<object>? tools = null)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (context.History == null || !context.History.Any()) throw new ArgumentException("History cannot be null or empty", nameof(context.History));

        var latestUserMessage = context.History
            .Where(m => !m.IsFromAi && !string.IsNullOrWhiteSpace(m.Content))
            .LastOrDefault();

        if (latestUserMessage == null)
        {
            throw new InvalidOperationException("Could not find a valid user message in the history to use as a prompt.");
        }
        
        int numImages = context.NumImages ?? 1;
        string imageSize = context.ImageSize ?? "1024x1024";

        var instance = new ImagenInstance
        {
            Prompt = latestUserMessage.Content,
            NumberOfImages = numImages,
            Size = imageSize
        };

        var payload = new ImagenPayload
        {
            Instances = new[] { instance }
        };
        return new AiRequestPayload(payload);
    }
    
    public Task<AiRequestPayload> PreparePayloadAsync(AiRequestContext context, List<object>? tools = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PreparePayload(context, tools));
    }
}

public interface IPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context, List<object>? tools = null);
}