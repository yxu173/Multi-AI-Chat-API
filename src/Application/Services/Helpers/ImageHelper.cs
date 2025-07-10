using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Application.Services.Helpers;

public static class ImageHelper
{
    public static (int width, int height) GetImageDimensions(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
            throw new ArgumentException("Base64 data is null or empty.", nameof(base64Data));

        byte[] imageBytes = Convert.FromBase64String(base64Data);

        using (var ms = new System.IO.MemoryStream(imageBytes))
        {
            var imageInfo = Image.Identify(ms);
            if (imageInfo == null)
                throw new InvalidOperationException("Unable to identify image format or decode image.");

            return (imageInfo.Width, imageInfo.Height);
        }
    }
} 