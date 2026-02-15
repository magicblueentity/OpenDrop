using OpenDrop.Core.Security;
using Xunit;

namespace OpenDrop.Tests;

public sealed class PathSafetyTests
{
    [Fact]
    public void SanitizeFileName_DropsPathTraversal()
    {
        Assert.Equal("evil.txt", PathSafety.SanitizeFileName("..\\..\\evil.txt"));
        Assert.Equal("evil.txt", PathSafety.SanitizeFileName("../../evil.txt"));
    }

    [Fact]
    public void SafeJoin_StaysWithinBaseDirectory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "airdroplike-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(baseDir);

        var p = PathSafety.SafeJoin(baseDir, "..\\..\\evil.txt");
        Assert.StartsWith(Path.GetFullPath(baseDir), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase);
    }
}
