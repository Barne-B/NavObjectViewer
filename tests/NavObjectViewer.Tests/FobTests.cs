using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

public class FobTests
{
    [Fact]
    public void Indexes_objects_with_metadata()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        Assert.Equal(3, objs.Count);
        NavObject cu = objs.Single(o => o.Id == 50001);
        Assert.Equal(NavObjectType.Codeunit, cu.Type);
        Assert.Equal("Test Mgt.", cu.Name);
        Assert.Equal("TEST1.00", cu.VersionList);
        Assert.Equal("23.01.25", cu.Date);
        Assert.Equal("01:00:00", cu.Time);
        Assert.True(cu.Modified);
        Assert.True(cu.IsFob);
        Assert.Equal(NavObjectType.Page, objs.Single(o => o.Id == 50002).Type);
    }

    [Fact]
    public void Detects_fob_by_content_without_extension()
    {
        using var f = SyntheticFob.CreateDefault();
        string renamed = f.Path + ".bin";
        File.Copy(f.Path, renamed);
        try
        {
            Assert.True(FobReader.IsFob(renamed));
            Assert.Equal(3, new ObjectIndexer().IndexFile(renamed).Count);
        }
        finally
        {
            File.Delete(renamed);
        }
    }

    [Fact]
    public void Decoded_text_contains_functions_and_code()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        string text = ObjectReader.ReadText(objs.Single(o => o.Id == 50001));
        Assert.StartsWith("OBJECT Codeunit 50001 Test Mgt.", text);
        Assert.Contains("    Modified=Yes;", text);
        Assert.Contains("    DoWork() : Boolean\r\n    BEGIN\r\n      // Explain the work\r\n      MESSAGE('Grüße');", text);
        Assert.Contains("    OnAfterWork(VAR Handled : Boolean)\r\n    // [Integration]\r\n    BEGIN\r\n    END;", text);

        string page = ObjectReader.ReadText(objs.Single(o => o.Id == 50002));
        Assert.Contains("no stored C/AL code", page);
    }

    [Fact]
    public void Search_finds_code_inside_fob_objects()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        List<ObjectHit> hits = new FullTextSearch().SearchObjects(objs, "testfield", ignoreCase: true, useRegex: false);
        Assert.Equal(50000, Assert.Single(hits).Object.Id);
    }

    // C/SIDE pads the object list with 1-4 0x1A bytes; name lengths 31..34 make the
    // two header lines 163..166 bytes long, which covers every padding length.
    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    public void Reads_files_with_any_padding_length(int nameLength)
    {
        string name = new('N', nameLength);
        using var f = SyntheticFob.Create([new SyntheticFob.Obj(5, "Codeunit", 50010, name, false, "OnRun()\r\n  EXIT;\r\n")]);

        NavObject obj = Assert.Single(new ObjectIndexer().IndexFile(f.Path));
        Assert.Equal(name, obj.Name);
        Assert.Contains("EXIT;", ObjectReader.ReadText(obj));
    }

    [Fact]
    public void Text_export_of_fob_objects_writes_the_decoded_view()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        string outFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navtest-out-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            new ObjectExporter().ExportCombined(objs, outFile);
            List<NavObject> reread = new ObjectIndexer().IndexFile(outFile);
            Assert.Equal(3, reread.Count);
            Assert.Contains("MESSAGE('Grüße');", ObjectReader.ReadText(reread.Single(o => o.Id == 50001)));
        }
        finally
        {
            File.Delete(outFile);
        }
    }
}
