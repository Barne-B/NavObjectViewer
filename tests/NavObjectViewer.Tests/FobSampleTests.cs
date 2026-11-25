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
    public void Every_object_of_the_sample_decodes_to_code()
    {
        if (SamplePath is not string path)
            return;

        List<NavObject> objs = new ObjectIndexer().IndexFile(path);
        Assert.NotEmpty(objs);
        Assert.All(objs, o =>
        {
            Assert.NotNull(FobReader.ReadCode(o));
            Assert.Contains("    BEGIN", ObjectReader.ReadText(o));
        });
    }
}
