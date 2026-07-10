using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

public class ObjectIndexerTests
{
    private static List<NavObject> Index(SyntheticExport f) => new ObjectIndexer().IndexFile(f.Path);

    [Fact]
    public void Indexes_all_objects()
    {
        using var f = SyntheticExport.CreateDefault();
        Assert.Equal(4, Index(f).Count);
    }

    [Fact]
    public void Parses_plain_header_and_metadata()
    {
        using var f = SyntheticExport.CreateDefault();
        NavObject t = Index(f).Single(o => o.Type == NavObjectType.Table && o.Id == 3);

        Assert.Equal("Payment Terms", t.Name);
        Assert.Equal("NAVW114.00", t.VersionList);
        Assert.Equal("24.03.19", t.Date);
        Assert.Equal("12:00:00", t.Time);
        Assert.False(t.Modified);
        Assert.False(t.IsAnomalous);
    }

    [Fact]
    public void Parses_name_containing_slash()
    {
        using var f = SyntheticExport.CreateDefault();
        NavObject t = Index(f).Single(o => o.Id == 9);
        Assert.Equal("Country/Region", t.Name);
    }

    [Fact]
    public void Detects_modified_flag()
    {
        using var f = SyntheticExport.CreateDefault();
        NavObject c = Index(f).Single(o => o.Type == NavObjectType.Codeunit && o.Id == 80040);
        Assert.True(c.Modified);
        Assert.Equal("Buchungsfunktionen Mgt.", c.Name);
    }

    [Fact]
    public void Parses_bracketed_header_as_anomalous()
    {
        using var f = SyntheticExport.CreateDefault();
        NavObject r = Index(f).Single(o => o.Type == NavObjectType.Report);

        Assert.Equal(50014, r.Id);
        Assert.Equal("Reise SR buchen abschließen", r.Name); // bracket + trailing space stripped
        Assert.True(r.IsAnomalous);
        Assert.True(r.Modified);
    }

    [Fact]
    public void Byte_ranges_are_contiguous_and_end_at_eof()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = Index(f);

        for (int i = 1; i < objs.Count; i++)
            Assert.Equal(objs[i - 1].EndOffset, objs[i].StartOffset);

        Assert.Equal(0, objs[0].StartOffset);
        Assert.Equal(new FileInfo(f.Path).Length, objs[^1].EndOffset);
    }

    [Fact]
    public void Reads_object_text_decoded_as_cp850()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = Index(f);

        string codeText = ObjectReader.ReadText(objs.Single(o => o.Id == 80040));
        Assert.Contains("PROCEDURE PostSomething", codeText);

        string tableText = ObjectReader.ReadText(objs.Single(o => o.Id == 3));
        Assert.Contains("Fälligkeit", tableText); // proves CP850 decode (0x84 → ä)
    }
}
