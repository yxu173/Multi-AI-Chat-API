using Application.Services.Files;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Infrastructure.Services.FileStorage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _uploadsBasePath;
    private readonly ILogger<LocalFileStorageService> _logger;
    private const int StreamBufferSize = 81920;

    private static readonly ActivitySource ActivitySource = new("Infrastructure.Services.FileStorage.LocalFileStorageService", "1.0.0");

    public LocalFileStorageService(string uploadsBasePath, ILogger<LocalFileStorageService> logger)
    {
        _uploadsBasePath = uploadsBasePath ?? throw new ArgumentNullException(nameof(uploadsBasePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Directory.Exists(_uploadsBasePath))
        {
            Directory.CreateDirectory(_uploadsBasePath);
        }
    }

    private string GetDailyUploadPath(string fileName)
    {
        var dailyUploadPath = Path.Combine(_uploadsBasePath, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        if (!Directory.Exists(dailyUploadPath))
        {
            Directory.CreateDirectory(dailyUploadPath);
        }
        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
        return Path.Combine(dailyUploadPath, uniqueFileName);
    }
    
    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(SaveFileAsync) + "_Stream");
        var filePath = GetDailyUploadPath(fileName);
        activity?.SetTag("file.path", filePath);

        await using var newFileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize, useAsync: true);
        if (fileStream.CanSeek)
        {
            fileStream.Seek(0, SeekOrigin.Begin);
        }
        await fileStream.CopyToAsync(newFileStream, StreamBufferSize, cancellationToken);

        _logger.LogInformation("File {FileName} saved to {FilePath} from stream.", fileName, filePath);
        return filePath;
    }

    public async Task<string> SaveFileAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(SaveFileAsync) + "_Bytes");
        var filePath = GetDailyUploadPath(fileName);
        activity?.SetTag("file.path", filePath);

        await File.WriteAllBytesAsync(filePath, fileBytes, cancellationToken);
        
        _logger.LogInformation("File {FileName} saved to {FilePath} from byte array.", fileName, filePath);
        return filePath;
    }

    public async Task<byte[]> ReadFileAsBytesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadFileAsBytesAsync));
        activity?.SetTag("file.path", filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Attempted to read non-existent file: {FilePath}", filePath);
            throw new FileNotFoundException("The requested file was not found.", filePath);
        }

        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }
    
    public Task<Stream> ReadFileAsStreamAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(ReadFileAsStreamAsync));
        activity?.SetTag("file.path", filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Attempted to read non-existent file: {FilePath}", filePath);
            throw new FileNotFoundException("The requested file was not found.", filePath);
        }

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, useAsync: true);
        return Task.FromResult<Stream>(stream);
    }

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(DeleteFileAsync));
        activity?.SetTag("file.path", filePath);

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("File deleted from {FilePath}", filePath);
            }
            else
            {
                _logger.LogWarning("Attempted to delete non-existent file: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file at {FilePath}", filePath);
        }

        return Task.CompletedTask;
    }
} 