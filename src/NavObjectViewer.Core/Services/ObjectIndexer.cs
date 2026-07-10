using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Builds a byte-offset index of every C/AL object in a text export by streaming
/// the file once. The file is never loaded into memory in full; only the header
/// and OBJECT-PROPERTIES lines are decoded (as CP850). Object bodies are read
/// later on demand via <see cref="ObjectReader"/>.
/// </summary>
public sealed partial class ObjectIndexer
{
    // "OBJECT " in ASCII — a header line always begins with this at column 0.
    private static readonly byte[] ObjectPrefix = "OBJECT "u8.ToArray();

    [GeneratedRegex(@"^OBJECT\s+(?<b>\[)?\s*(?<type>[A-Za-z]+)\s+(?<id>\d+)(?:\s+(?<name>.*?))?\s*\]?\s*$")]
    private static partial Regex HeaderRegex();

    private const int ChunkSize = 1 << 20; // 1 MiB read buffer

    /// <summary>Index a single export file. Reports fractional progress (0..1).</summary>
    public List<NavObject> IndexFile(string path, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var results = new List<NavObject>();
        Encoding enc = Encodings.Nav;
        long fileLength = new FileInfo(path).Length;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            ChunkSize, FileOptions.SequentialScan);

        byte[] buffer = new byte[ChunkSize];
        var carry = new List<byte>(256); // bytes of a line that spans a chunk boundary
        long lineStart = 0;              // absolute byte offset where the current line begins
        long chunkBase = 0;              // absolute byte offset of buffer[0]
        long lastReported = 0;

        NavObject? current = null;
        var props = new PropsState();

        // Handle one complete line (span excludes the trailing '\n', may include '\r').
        void ProcessLine(ReadOnlySpan<byte> line, long start, long endExclusive)
        {
            if (StartsWithObjectPrefix(line))
            {
                string text = DecodeLine(enc, line);
                Match m = HeaderRegex().Match(text);
                if (m.Success)
                {
                    // Close the previous object at this header's start.
                    if (current is not null)
                        current.EndOffset = start;

                    current = new NavObject
                    {
                        Type = NavObjectTypes.Parse(m.Groups["type"].Value),
                        Id = int.Parse(m.Groups["id"].Value),
                        Name = m.Groups["name"].Value.Trim(),
                        SourceFile = path,
                        StartOffset = start,
                        EndOffset = endExclusive, // provisional; fixed by next header or EOF
                        IsAnomalous = m.Groups["b"].Success,
                    };
                    results.Add(current);
                    props.Reset();
                    return;
                }
                // Prefix matched but not a valid header — treat as ordinary body line.
            }

            if (current is not null && !props.Done)
                props.Feed(DecodeLine(enc, line), current);
        }

        int read;
        while ((read = fs.Read(buffer, 0, ChunkSize)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            int segStart = 0;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\n')
                    continue;

                long endExclusive = chunkBase + i + 1;
                if (carry.Count > 0)
                {
                    carry.AddRange(buffer.AsSpan(segStart, i - segStart));
                    ProcessLine(CollectionsMarshal.AsSpan(carry), lineStart, endExclusive);
                    carry.Clear();
                }
                else
                {
                    ProcessLine(buffer.AsSpan(segStart, i - segStart), lineStart, endExclusive);
                }
                lineStart = endExclusive;
                segStart = i + 1;
            }

            if (segStart < read)
                carry.AddRange(buffer.AsSpan(segStart, read - segStart));

            chunkBase += read;
            if (progress is not null && chunkBase - lastReported > (1 << 22))
            {
                progress.Report((double)chunkBase / fileLength);
                lastReported = chunkBase;
            }
        }

        // Final line with no trailing newline.
        if (carry.Count > 0)
            ProcessLine(CollectionsMarshal.AsSpan(carry), lineStart, chunkBase);

        if (current is not null)
            current.EndOffset = fileLength;

        progress?.Report(1.0);
        return results;
    }

    /// <summary>Index every *.txt file in a directory (non-recursive by default).</summary>
    public List<NavObject> IndexDirectory(string directory, bool recursive = false,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] files = Directory.GetFiles(directory, "*.txt", option);
        var all = new List<NavObject>();
        for (int f = 0; f < files.Length; f++)
        {
            ct.ThrowIfCancellationRequested();
            all.AddRange(IndexFile(files[f], null, ct));
            progress?.Report((double)(f + 1) / files.Length);
        }
        return all;
    }

    private static bool StartsWithObjectPrefix(ReadOnlySpan<byte> line)
        => line.Length >= ObjectPrefix.Length && line[..ObjectPrefix.Length].SequenceEqual(ObjectPrefix);

    private static string DecodeLine(Encoding enc, ReadOnlySpan<byte> line)
    {
        if (line.Length > 0 && line[^1] == (byte)'\r')
            line = line[..^1];
        return enc.GetString(line);
    }

    /// <summary>Tracks the small OBJECT-PROPERTIES region to extract metadata cheaply.</summary>
    private sealed class PropsState
    {
        private bool _sawHeader;
        private int _depth;
        private bool _entered;
        public bool Done { get; private set; }

        public void Reset()
        {
            _sawHeader = false;
            _depth = 0;
            _entered = false;
            Done = false;
        }

        public void Feed(string line, NavObject obj)
        {
            if (Done)
                return;

            string t = line.Trim();
            if (!_sawHeader)
            {
                if (t == "OBJECT-PROPERTIES")
                    _sawHeader = true;
                return;
            }

            switch (t)
            {
                case "{":
                    _depth++;
                    _entered = true;
                    return;
                case "}":
                    _depth--;
                    if (_entered && _depth <= 0)
                        Done = true;
                    return;
            }

            AssignMeta(t, obj);
        }

        private static void AssignMeta(string line, NavObject obj)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
                return;

            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim().TrimEnd(';').Trim();
            switch (key)
            {
                case "Date":
                    obj.Date = val;
                    break;
                case "Time":
                    obj.Time = val;
                    break;
                case "Modified":
                    obj.Modified = val.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                    break;
                case "Version List":
                    obj.VersionList = val;
                    break;
            }
        }
    }
}
