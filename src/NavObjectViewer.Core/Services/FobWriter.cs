using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Writes a new .fob from objects indexed out of existing .fob files. Every object's
/// header lines, index chunks, compiled BLOB and runtime metadata are copied
/// byte-for-byte; only the container framing (settings chunk offsets/checksum and
/// the metadata count) is rebuilt. Objects from text exports cannot be written,
/// because turning C/AL text into a compiled object needs the C/SIDE compiler.
/// </summary>
public sealed class FobWriter
{
    private static readonly byte[] EndChunk = [0x02, 0x1B, 0, 0, 0, 0, 0, 0];

    // Settings TLVs holding an absolute offset into the container tail.
    private const ushort TagTailOffset32 = 0x1B13;
    private const ushort TagTailOffset64 = 0x1B1C;
    private const ushort TagObjectCount = 0x1B1E;
    private const long MaxTailDelta = 64;

    public ExportResult Write(IEnumerable<NavObject> objects, string outputFile,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var ordered = new List<NavObject>();
        var seen = new HashSet<(NavObjectType, int)>();
        foreach (NavObject o in objects.OrderBy(o => FobReader.NavTypeNumber(o.Type)).ThenBy(o => o.Id))
        {
            if (o.Fob is null)
            {
                warnings.Add($"{o} comes from a text export and was skipped — only .fob objects can be written to a .fob.");
                continue;
            }
            if (!seen.Add((o.Type, o.Id)))
            {
                warnings.Add($"Duplicate {o} from '{Path.GetFileName(o.SourceFile)}' was skipped.");
                continue;
            }
            ordered.Add(o);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException("None of the selected objects come from a .fob file.");

        string templatePath = ordered[0].SourceFile;
        FobLayout layout = FobReader.ReadLayout(templatePath);
        int sources = ordered.Select(o => o.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (sources > 1)
            warnings.Add($"Merged objects from {sources} .fob files; container settings were taken from '{Path.GetFileName(templatePath)}'.");

        long headerBytes = ordered.Sum(o => (long)o.Fob!.HeaderLength);
        byte[] padding = TextPadding(headerBytes);
        long newEndChunk = headerBytes + padding.Length + layout.PreambleLength
            + ordered.Sum(o => (long)o.Fob!.IndexLength)
            + ordered.Sum(o => o.ByteLength);

        byte[] preamble = FobReader.ReadRange(templatePath, layout.PreambleOffset, layout.PreambleLength);
        PatchSettings(preamble, layout.EndChunkOffset, newEndChunk, layout.ObjectCount, ordered.Count);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);

            foreach (NavObject o in ordered)
                Copy(Source(o), o.Fob!.HeaderOffset, o.Fob.HeaderLength, output);
            output.Write(padding);
            output.Write(preamble);

            foreach (NavObject o in ordered)
                Copy(Source(o), o.Fob!.IndexOffset, o.Fob.IndexLength, output);

            for (int i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Copy(Source(ordered[i]), ordered[i].StartOffset, ordered[i].ByteLength, output);
                progress?.Report(0.9 * (i + 1) / ordered.Count);
            }

            if (output.Position != newEndChunk)
                throw new InvalidOperationException("Internal error: .fob layout size mismatch.");

            output.Write(EndChunk);
            FileStream template = Source(ordered[0]);
            Copy(template, layout.HashChunkOffset, layout.HashChunkLength, output);

            List<NavObject> withMetadata = ordered.Where(o => o.Fob!.TrailerLength > 0).ToList();
            if (layout.HasTrailer)
            {
                Copy(template, layout.TrailerHeaderOffset, 12, output);
                output.Write(BitConverter.GetBytes(withMetadata.Count));
                foreach (NavObject o in withMetadata)
                    Copy(Source(o), o.Fob!.TrailerOffset, o.Fob.TrailerLength, output);
            }
            else if (withMetadata.Count > 0)
            {
                warnings.Add("The template .fob has no runtime metadata section, so the C/AL code streams were not written.");
            }
        }
        finally
        {
            foreach (FileStream fs in handles.Values)
                fs.Dispose();
        }

        progress?.Report(1.0);
        return new ExportResult(1, ordered.Count, outputFile, warnings);

        FileStream Source(NavObject o)
        {
            if (!handles.TryGetValue(o.SourceFile, out FileStream? fs))
            {
                fs = new FileStream(o.SourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                handles[o.SourceFile] = fs;
            }
            return fs;
        }
    }

    /// <summary>C/SIDE pads the text object list with 1-4 0x1A bytes up to a 4-byte boundary.</summary>
    public static byte[] TextPadding(long textLength)
        => Enumerable.Repeat(FobReader.TextPadding, 4 - (int)(textLength % 4)).ToArray();

    /// <summary>
    /// Rewrite the settings that depend on the object selection (end-of-objects
    /// offset, object count) and refresh the chunk checksum.
    /// </summary>
    /// <remarks>
    /// The settings TLVs are not uniformly length-prefixed (tag 0x1B12 declares 7 but
    /// carries 4 bytes), so tags are located by their tag/length signature and only
    /// rewritten when the value matches what the template file implies.
    /// </remarks>
    internal static void PatchSettings(byte[] chunk, long oldEnd, long newEnd, int oldCount, int newCount)
    {
        for (int pos = 8; pos + 4 <= chunk.Length; pos++)
        {
            ushort tag = BitConverter.ToUInt16(chunk, pos);
            ushort len = BitConverter.ToUInt16(chunk, pos + 2);
            int value = pos + 4;

            if (tag == TagTailOffset32 && len == 4 && value + 4 <= chunk.Length)
            {
                long v = BitConverter.ToUInt32(chunk, value);
                if (v - oldEnd is >= 0 and < MaxTailDelta)
                    BitConverter.TryWriteBytes(chunk.AsSpan(value, 4), (uint)(newEnd + (v - oldEnd)));
            }
            else if (tag == TagTailOffset64 && len == 8 && value + 8 <= chunk.Length)
            {
                long v = BitConverter.ToInt64(chunk, value);
                if (v - oldEnd is >= 0 and < MaxTailDelta)
                    BitConverter.TryWriteBytes(chunk.AsSpan(value, 8), newEnd + (v - oldEnd));
            }
            else if (tag == TagObjectCount && len == 8 && value + 8 <= chunk.Length
                && BitConverter.ToInt64(chunk, value) == oldCount)
            {
                BitConverter.TryWriteBytes(chunk.AsSpan(value, 8), (long)newCount);
            }
        }

        BitConverter.TryWriteBytes(chunk.AsSpan(2, 2), Checksum(chunk.AsSpan(8)));
    }

    /// <summary>Chunk checksum: the byte sum of the payload, truncated to 16 bits.</summary>
    public static ushort Checksum(ReadOnlySpan<byte> payload)
    {
        uint sum = 0;
        foreach (byte b in payload)
            sum += b;
        return (ushort)sum;
    }

    private static void Copy(FileStream source, long offset, long length, Stream output)
    {
        if (length <= 0)
            return;

        source.Position = offset;
        var buffer = new byte[(int)Math.Min(length, 1 << 16)];
        long remaining = length;
        while (remaining > 0)
        {
            int n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (n == 0)
                throw new EndOfStreamException($"Unexpected end of '{source.Name}'.");
            output.Write(buffer, 0, n);
            remaining -= n;
        }
    }
}
