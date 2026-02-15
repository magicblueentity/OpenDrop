using System.Text;

namespace AirDropLike.Core.Security;

public static class PathSafety
{
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "file";

        // Keep only a base file name; drop any path components.
        fileName = Path.GetFileName(fileName);

        var sb = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            if (ch == '\0') continue;
            if (Path.GetInvalidFileNameChars().Contains(ch)) { sb.Append('_'); continue; }
            sb.Append(ch);
        }

        var sanitized = sb.ToString().Trim();
        if (sanitized.Length == 0) sanitized = "file";
        if (sanitized is "." or "..") sanitized = "file";
        return sanitized;
    }

    public static string SafeJoin(string baseDirectory, string fileName)
    {
        baseDirectory = Path.GetFullPath(baseDirectory);
        var combined = Path.GetFullPath(Path.Combine(baseDirectory, SanitizeFileName(fileName)));
        if (!combined.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe path.");
        return combined;
    }
}
