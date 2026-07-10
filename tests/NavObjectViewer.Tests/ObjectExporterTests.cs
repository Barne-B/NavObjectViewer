using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

public class ObjectExporterTests
{
    private static byte[] TrimTrailing(byte[] b)
    {
        int end = b.Length;
        while (end > 0 && (b[end - 1] == (byte)'\r' || b[end - 1] == (byte)'\n'))
            end--;
        return b[..end];
    }

    [Fact]
    public void Combined_export_round_trips_byte_identically()
    {
        using var f = SyntheticExport.CreateDefault();
        var indexer = new ObjectIndexer();
        List<NavObject> objs = indexer.IndexFile(f.Path);
        List<NavObject> subset = objs.Where(o => o.Id is 3 or 80040).ToList();

        string outFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "navtest-combined-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            ExportResult result = new ObjectExporter().ExportCombined(subset, outFile);
            Assert.Equal(2, result.ObjectCount);

            List<NavObject> reindexed = indexer.IndexFile(outFile);
            Assert.Equal(2, reindexed.Count);

            foreach (NavObject r in reindexed)
            {
                NavObject orig = subset.Single(o => o.Type == r.Type && o.Id == r.Id);
                Assert.Equal(TrimTrailing(ObjectReader.ReadBytes(orig)), TrimTrailing(ObjectReader.ReadBytes(r)));
            }
        }
        finally
        {
            File.Delete(outFile);
        }
    }

    [Fact]
    public void Per_object_export_uses_prefix_and_type_folders()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        string outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "navtest-per-" + Guid.NewGuid().ToString("N"));
        try
        {
            ExportResult result = new ObjectExporter().ExportPerObject(objs, outDir, groupByType: true);
            Assert.Equal(objs.Count, result.FileCount);
            Assert.True(File.Exists(System.IO.Path.Combine(outDir, "Table", "TAB3.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(outDir, "Codeunit", "COD80040.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(outDir, "Report", "REP50014.txt")));
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Duplicate_ids_are_disambiguated_with_a_warning()
    {
        using var a = SyntheticExport.CreateSingleTable(50000, "First");
        using var b = SyntheticExport.CreateSingleTable(50000, "Second");
        var indexer = new ObjectIndexer();
        List<NavObject> objs = [.. indexer.IndexFile(a.Path), .. indexer.IndexFile(b.Path)];

        string outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "navtest-dup-" + Guid.NewGuid().ToString("N"));
        try
        {
            ExportResult result = new ObjectExporter().ExportPerObject(objs, outDir, groupByType: true);

            Assert.Equal(2, result.FileCount);
            Assert.Single(result.Warnings);
            Assert.True(File.Exists(System.IO.Path.Combine(outDir, "Table", "TAB50000.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(outDir, "Table", "TAB50000_2.txt")));
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }
}
