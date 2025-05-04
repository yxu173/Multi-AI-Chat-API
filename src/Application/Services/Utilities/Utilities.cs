using System.Text.RegularExpressions;

namespace Application.Services.Utilities;

public static class Utilities
{
    public static List<Guid> ExtractFileAttachmentIds(string content)
    {
        var fileIds = new List<Guid>();
        var imageMatches = Regex.Matches(content, @"<(image|file):[^>]*?/api/file/([0-9a-fA-F-]{36})",
            RegexOptions.IgnoreCase);
        foreach (Match match in imageMatches)
        {
            if (Guid.TryParse(match.Groups[2].Value, out Guid fileId))
            {
                fileIds.Add(fileId);
            }
        }

        return fileIds;
    }
}