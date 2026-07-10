using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

public class IndexCacheTests
{
    [Fact]
    public void Save_then_load_round_trips_including_first_object_at_offset_zero()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        IndexCache.Save(f.Path, objs);
        List<NavObject>? loaded = IndexCache.TryLoad(f.Path);

        Assert.NotNull(loaded);
        Assert.Equal(objs.Count, loaded!.Count);

        // The first object starts at byte 0 — the case that previously broke the cache.
        Assert.Equal(0, loaded[0].StartOffset);

        for (int i = 0; i < objs.Count; i++)
        {
            Assert.Equal(objs[i].Type, loaded[i].Type);
            Assert.Equal(objs[i].Id, loaded[i].Id);
            Assert.Equal(objs[i].Name, loaded[i].Name);
            Assert.Equal(objs[i].StartOffset, loaded[i].StartOffset);
            Assert.Equal(objs[i].EndOffset, loaded[i].EndOffset);
            Assert.Equal(objs[i].Modified, loaded[i].Modified);
            Assert.Equal(objs[i].IsAnomalous, loaded[i].IsAnomalous);
            Assert.Equal(objs[i].VersionList, loaded[i].VersionList);
        }
    }
}
