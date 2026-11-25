using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

/// <summary>
/// Checks against a real .fob. Set NAV_FOB_SAMPLE to its path to run them; they
/// pass trivially otherwise, so no customer objects need to live in the repository.
/// </summary>
public class FobSampleTests
{
    private static string? SamplePath =>
        Environment.GetEnvironmentVariable("NAV_FOB_SAMPLE") is { Length: > 0 } p && File.Exists(p) ? p : null;

    [Fact]
    public void Rewriting_every_object_reproduces_the_sample_byte_for_byte()
    {
        if (SamplePath is not string path)
            return;

        List<NavObject> objs = new ObjectIndexer().IndexFile(path);
        Assert.NotEmpty(objs);
        Assert.All(objs, o => Assert.NotNull(FobReader.ReadCode(o)));

        string outFile = Path.Combine(Path.GetTempPath(), "navtest-fob-" + Guid.NewGuid().ToString("N") + ".fob");
        try
        {
            new FobWriter().Write(objs, outFile);
            Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(outFile));
        }
        finally
        {
            File.Delete(outFile);
        }
    }
}
