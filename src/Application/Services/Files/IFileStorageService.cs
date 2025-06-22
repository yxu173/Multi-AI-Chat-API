namespace Application.Services.Files;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<string> SaveFileAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default);
    Task<byte[]> ReadFileAsBytesAsync(string filePath, CancellationToken cancellationToken = default);
    Task<Stream> ReadFileAsStreamAsync(string filePath, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
} 