using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

public sealed record ExportResult(int FileCount, int ObjectCount, string OutputPath, IReadOnlyList<string> Warnings);

/// <summary>
/// Exports selected objects by copying their raw source bytes (no re-encoding), so
/// the output re-imports into C/SIDE with byte-for-byte fidelity. Supports one
/// combined file or one file per object grouped into per-type subfolders.
/// </summary>
public sealed class ObjectExporter
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    /// <summary>Concatenate the selected objects (sorted Type then Id) into a single .txt file.</summary>
    public ExportResult ExportCombined(IEnumerable<NavObject> objects, string outputFile,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        List<NavObject> ordered = Order(objects);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);

        using (var outStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                WriteObjectBlock(outStream, ordered[i]);
                progress?.Report((double)(i + 1) / ordered.Count);
            }
        }

        return new ExportResult(1, ordered.Count, outputFile, []);
    }

    /// <summary>Write each selected object to its own &lt;Prefix&gt;&lt;Id&gt;.txt file.</summary>
    public ExportResult ExportPerObject(IEnumerable<NavObject> objects, string outputDir,
        bool groupByType = true, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        List<NavObject> ordered = Order(objects);
        var warnings = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fileCount = 0;

        for (int i = 0; i < ordered.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            NavObject obj = ordered[i];

            string dir = groupByType ? Path.Combine(outputDir, obj.TypeName) : outputDir;
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, obj.FileName);
            if (!used.Add(path))
            {
                path = DisambiguateName(dir, obj, used);
                warnings.Add($"Duplicate {obj.TypeName} {obj.Id} — written as '{Path.GetFileName(path)}'.");
            }

            using (var outStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                WriteObjectBlock(outStream, obj);

            fileCount++;
            progress?.Report((double)(i + 1) / ordered.Count);
        }

        return new ExportResult(fileCount, ordered.Count, outputDir, warnings);
    }

    private static List<NavObject> Order(IEnumerable<NavObject> objects)
        => objects.OrderBy(o => o.Type).ThenBy(o => o.Id).ThenBy(o => o.SourceFile).ToList();

    // Writes the object's meaningful bytes (trailing blank lines normalized) followed
    // by a blank-line separator, so re-import sees cleanly delimited objects.
    private static void WriteObjectBlock(Stream outStream, NavObject obj)
    {
        byte[] bytes = ObjectReader.ReadBytes(obj);
        int end = bytes.Length;
        while (end > 0 && (bytes[end - 1] == (byte)'\r' || bytes[end - 1] == (byte)'\n'))
            end--;

        outStream.Write(bytes, 0, end);
        outStream.Write(Crlf, 0, Crlf.Length);
        outStream.Write(Crlf, 0, Crlf.Length);
    }

    private static string DisambiguateName(string dir, NavObject obj, HashSet<string> used)
    {
        string stem = $"{obj.Type.Prefix()}{obj.Id}";
        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(dir, $"{stem}_{n}.txt");
            if (used.Add(candidate))
                return candidate;
        }
    }
}
