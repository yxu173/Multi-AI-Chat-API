namespace Application.Services.AI.RequestHandling.Models;

public record FileBase64Data
{
    public required string Base64Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public required string FileType { get; init; }
}
