using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

public class FobTests
{
    private static string TempFob() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navtest-out-" + Guid.NewGuid().ToString("N") + ".fob");

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

    [Fact]
    public void Rewriting_all_objects_is_byte_identical()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        string outFile = TempFob();
        try
        {
            new FobWriter().Write(objs, outFile);
            Assert.Equal(File.ReadAllBytes(f.Path), File.ReadAllBytes(outFile));
        }
        finally
        {
            File.Delete(outFile);
        }
    }

    [Fact]
    public void Subset_fob_matches_a_fob_built_from_only_those_objects()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        string outFile = TempFob();
        try
        {
            ExportResult result = new FobWriter().Write(objs.Where(o => o.Id != 50000), outFile);
            Assert.Equal(2, result.ObjectCount);

            byte[] expected = SyntheticFob.Build(SyntheticFob.DefaultObjects.Where(o => o.Id != 50000).ToList());
            Assert.Equal(expected, File.ReadAllBytes(outFile));

            List<NavObject> reread = new ObjectIndexer().IndexFile(outFile);
            Assert.Equal([50001, 50002], reread.Select(o => o.Id));
            Assert.Contains("EXIT(TRUE);", ObjectReader.ReadText(reread[0]));
        }
        finally
        {
            File.Delete(outFile);
        }
    }

    [Fact]
    public void Writer_skips_text_objects_and_rejects_text_only_selections()
    {
        using var fob = SyntheticFob.CreateDefault();
        using var txt = SyntheticExport.CreateDefault();
        var indexer = new ObjectIndexer();
        List<NavObject> textObjs = indexer.IndexFile(txt.Path);
        List<NavObject> fobObjs = indexer.IndexFile(fob.Path);
        string outFile = TempFob();
        try
        {
            Assert.Throws<InvalidOperationException>(() => new FobWriter().Write(textObjs, outFile));

            ExportResult result = new FobWriter().Write(fobObjs.Concat(textObjs.Take(1)), outFile);
            Assert.Equal(3, result.ObjectCount);
            Assert.Contains(result.Warnings, w => w.Contains("text export"));
        }
        finally
        {
            File.Delete(outFile);
        }
    }

    [Fact]
    public void Text_export_of_fob_objects_writes_the_decoded_view()
    {
        using var f = SyntheticFob.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        string outFile = System.IO.Path.ChangeExtension(TempFob(), ".txt");
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
