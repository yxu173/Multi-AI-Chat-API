using Domain.Aggregates.Chats;
using Domain.Common;
using Domain.Repositories;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Application.Services.Files;

public class FileUploadService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly string _uploadsBasePath;

    public FileUploadService(IFileAttachmentRepository fileAttachmentRepository, string uploadsBasePath)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _uploadsBasePath = uploadsBasePath ?? throw new ArgumentNullException(nameof(uploadsBasePath));
        
        // Ensure uploads directory exists
        if (!Directory.Exists(_uploadsBasePath))
        {
            Directory.CreateDirectory(_uploadsBasePath);
        }
    }

    public async Task<FileAttachment> UploadFileAsync(IFormFile file, Guid? messageId = null, CancellationToken cancellationToken = default)
    {
        var uploadPath = Path.Combine(_uploadsBasePath, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var filePath = Path.Combine(uploadPath, uniqueFileName);

        byte[] fileBytes;
        
        if (file.ContentType.StartsWith("image/"))
        {
            using var image = await Image.LoadAsync(file.OpenReadStream());
            

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(2048, 2048),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            }));

            await using var ms = new MemoryStream();
            IImageEncoder encoder = file.ContentType switch
            {
                "image/jpeg" => new JpegEncoder { Quality = 75 },
                "image/png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
                "image/webp" => new WebpEncoder { Quality = 80 },
                _ => new JpegEncoder() { Quality = 75 }
            };

            await image.SaveAsync(ms, encoder, cancellationToken);
            fileBytes = ms.ToArray();
        }
        else
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            fileBytes = stream.ToArray();
        }

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await stream.WriteAsync(fileBytes, 0, fileBytes.Length, cancellationToken);
        }

        var fileAttachment = FileAttachment.Create(
            file.FileName,
            filePath,
            file.ContentType,
            file.Length,
            messageId
        );

        // Save to database
        await _fileAttachmentRepository.AddAsync(fileAttachment, cancellationToken);

        return fileAttachment;
    }

    public async Task AssociateFileWithMessageAsync(Guid fileId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
        {
            throw new InvalidOperationException($"File attachment with ID {fileId} not found");
        }

        var updatedFileAttachment = FileAttachment.Create(
            fileAttachment.FileName,
            fileAttachment.FilePath,
            fileAttachment.ContentType,
            fileAttachment.FileSize,
            messageId
        );

         var fieldInfo = typeof(BaseEntity).GetField("<Id>k__BackingField", 
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (fieldInfo != null)
        {
            fieldInfo.SetValue(updatedFileAttachment, fileAttachment.Id);
        }

        await _fileAttachmentRepository.UpdateAsync(updatedFileAttachment, cancellationToken);
    }

    public async Task<string> GetFileUrlAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
        {
            throw new InvalidOperationException($"File attachment with ID {fileId} not found");
        }

        return fileAttachment.FilePath;
    }

    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
        {
            return; 
        }

        if (File.Exists(fileAttachment.FilePath))
        {
            File.Delete(fileAttachment.FilePath);
        }

        // Remove from database
        await _fileAttachmentRepository.DeleteAsync(fileId, cancellationToken);
    }
} 