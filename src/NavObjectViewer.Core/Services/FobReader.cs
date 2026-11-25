using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Reads the binary C/SIDE object container (.fob, NAV 2013 and later).
///
/// Layout, as reverse-engineered from real files:
/// <code>
///   text object list   2 CRLF lines per object (type/id/name/date/time, size/version list)
///   1A padding         1-4 bytes, so the chunks start on a 4-byte boundary
///   chunks             [u16 kind][u16 checksum = byte sum of payload][u32 length][payload]
///     0x1B01           container settings (nested u16 tag/u16 length TLVs, collation tables;
///                      0x1B13/0x1B1C = end-of-objects offset + 10, 0x1B1E = object count)
///     1 + 4            per object: Object-table record + size info (the index; 16 or 56 bytes,
///                      the longer form also carries the version list)
///     2 + 3            per object: Object-table record + compiled object BLOB
///     0x1B02           end of objects (empty)
///     0x1B03           16-byte checksum (keyed; not reproducible, so .fob files are read-only here)
///   runtime metadata   u32 magic, u32, u32, u32 count, then per object:
///                      u32 type, u32 id, u32 0, then 2x (u32 length + raw deflate):
///                      generated C# and the C/AL source used by the debugger
/// </code>
/// The compiled BLOB is an undocumented binary format; readable text comes from
/// the C/AL code stream, which holds every trigger and function with its body.
/// </summary>
public static partial class FobReader
{
    public const ushort KindIndexRecord = 1;
    public const ushort KindData = 2;
    public const ushort KindBlob = 3;
    public const ushort KindIndexInfo = 4;
    public const ushort KindPreamble = 0x1B01;
    public const ushort KindEnd = 0x1B02;
    public const ushort KindContainerId = 0x1B03;

    /// <summary>Pads the text object list up to the 4-byte-aligned start of the chunks.</summary>
    public const byte TextPadding = 0x1A;

    [GeneratedRegex(@"^(?<type>Table|Form|Report|Dataport|Codeunit|XMLport|MenuSuite|Page|Query)\s+\d+\s", RegexOptions.IgnoreCase)]
    private static partial Regex FirstLineRegex();

    [GeneratedRegex(@"\s(?<date>\S+)\s+(?<time>\d{1,2}:\d{2}:\d{2})\s*$")]
    private static partial Regex DateTimeRegex();

    /// <summary>True when the file is a binary object container (by extension or content).</summary>
    public static bool IsFob(string path)
    {
        if (path.EndsWith(".fob", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[256];
            int n = fs.Read(buffer, 0, buffer.Length);
            return FirstLineRegex().IsMatch(Encoding.ASCII.GetString(buffer, 0, n));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Index every object of a .fob file.</summary>
    public static List<NavObject> Index(string path, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var reader = new BinaryReader(fs);

        List<string> headers = ReadHeaderList(fs, out long binaryStart);
        fs.Position = binaryStart;
        ReadChunkHeader(reader, out ushort kind, out _, out uint len);
        if (kind != KindPreamble)
            throw new InvalidDataException("Not a supported .fob file: container settings chunk is missing.");
        fs.Position += len;

        var data = new List<(long Offset, long End, byte[] Record)>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long chunkStart = fs.Position;
            ReadChunkHeader(reader, out kind, out _, out len);
            long next = fs.Position + len;
            if (next > fs.Length)
                throw new InvalidDataException($"Truncated .fob chunk at offset {chunkStart}.");

            switch (kind)
            {
                case KindIndexRecord:
                case KindIndexInfo:
                    break;
                case KindData:
                    data.Add((chunkStart, next, reader.ReadBytes((int)len)));
                    break;
                case KindBlob when data.Count > 0:
                    data[^1] = (data[^1].Offset, next, data[^1].Record);
                    break;
                case KindEnd:
                    break;
                default:
                    throw new InvalidDataException($"Unsupported .fob chunk kind 0x{kind:X4} at offset {chunkStart}.");
            }

            fs.Position = next;
            if (kind == KindEnd)
                break;
            progress?.Report(0.9 * fs.Position / fs.Length);
        }

        Dictionary<(int Type, int Id), (long Offset, int Length)> trailer = ReadTrailer(fs, reader);

        var results = new List<NavObject>(data.Count);
        for (int i = 0; i < data.Count; i++)
        {
            ObjectRecord rec = ObjectRecord.Parse(data[i].Record);
            string headerLine = i < headers.Count ? headers[i] : string.Empty;
            Match dt = DateTimeRegex().Match(headerLine);

            trailer.TryGetValue((rec.Type, rec.Id), out var meta);
            results.Add(new NavObject
            {
                Type = MapType(rec.Type),
                Id = rec.Id,
                Name = rec.Name,
                SourceFile = path,
                StartOffset = data[i].Offset,
                EndOffset = data[i].End,
                VersionList = rec.VersionList,
                Modified = rec.Modified,
                Date = dt.Success ? dt.Groups["date"].Value : rec.FormatDate(),
                Time = dt.Success ? dt.Groups["time"].Value : rec.FormatTime(),
                Fob = new FobEntry
                {
                    TrailerOffset = meta.Offset,
                    TrailerLength = meta.Length,
                    Compiled = rec.Compiled,
                },
            });
        }

        progress?.Report(1.0);
        return results;
    }

    /// <summary>The object's C/AL source (triggers and functions with bodies), or null when not stored.</summary>
    public static string? ReadCode(NavObject obj) => ReadTrailerStream(obj, 1);

    /// <summary>The C# the NAV server generated for the object, or null when not stored.</summary>
    public static string? ReadGeneratedCSharp(NavObject obj) => ReadTrailerStream(obj, 0);

    private static string? ReadTrailerStream(NavObject obj, int streamIndex)
    {
        if (obj.Fob is not { TrailerLength: > 0 } fob)
            return null;

        byte[] entry = ReadRange(obj.SourceFile, fob.TrailerOffset, fob.TrailerLength);
        int pos = 12;
        for (int i = 0; i <= streamIndex; i++)
        {
            if (pos + 4 > entry.Length)
                return null;
            int length = BitConverter.ToInt32(entry, pos);
            pos += 4;
            if (length < 0 || pos + length > entry.Length)
                return null;
            if (i == streamIndex)
                return DecodeText(Inflate(entry, pos, length));
            pos += length;
        }
        return null;
    }

    private static byte[] ReadRange(string path, long offset, int length)
    {
        var bytes = new byte[length];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Position = offset;
        fs.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] Inflate(byte[] buffer, int offset, int length)
    {
        using var input = new MemoryStream(buffer, offset, length);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private static string DecodeText(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encodings.Nav.GetString(bytes);
        }
    }

    private static List<string> ReadHeaderList(FileStream fs, out long binaryStart)
    {
        var lines = new List<string>();
        var current = new List<byte>(160);
        long pos = 0;
        bool inPadding = false;
        var buffer = new byte[1 << 16];
        fs.Position = 0;

        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++, pos++)
            {
                byte b = buffer[i];
                if (b == TextPadding)
                {
                    inPadding = true;
                    continue;
                }
                if (inPadding)
                {
                    binaryStart = pos;
                    return PairLines(lines);
                }

                current.Add(b);
                if (b != (byte)'\n')
                    continue;

                lines.Add(Encodings.Nav.GetString(current.ToArray()).TrimEnd('\r', '\n'));
                current.Clear();
            }
        }

        throw new InvalidDataException("Not a .fob file: the end of the object list was not found.");
    }

    // Each object occupies two lines; the first carries type/id/name/date/time.
    private static List<string> PairLines(List<string> lines)
    {
        var firstLines = new List<string>(lines.Count / 2);
        for (int i = 0; i + 1 < lines.Count; i += 2)
            firstLines.Add(lines[i]);
        return firstLines;
    }

    private static Dictionary<(int, int), (long, int)> ReadTrailer(FileStream fs, BinaryReader reader)
    {
        var result = new Dictionary<(int, int), (long, int)>();
        if (TryPeekChunk(reader, KindContainerId, out uint hashLen))
            fs.Position += 8 + hashLen;

        if (fs.Position + 16 > fs.Length)
            return result;

        fs.Position += 12;
        int count = reader.ReadInt32();
        for (int i = 0; i < count && fs.Position + 12 <= fs.Length; i++)
        {
            long start = fs.Position;
            int type = reader.ReadInt32();
            int id = reader.ReadInt32();
            reader.ReadInt32();
            for (int s = 0; s < 2; s++)
            {
                if (fs.Position + 4 > fs.Length)
                    return result;
                uint length = reader.ReadUInt32();
                if (fs.Position + length > fs.Length)
                    return result;
                fs.Position += length;
            }
            result.TryAdd((type, id), (start, (int)(fs.Position - start)));
        }
        return result;
    }

    private static bool TryPeekChunk(BinaryReader reader, ushort expectedKind, out uint length)
    {
        length = 0;
        Stream s = reader.BaseStream;
        if (s.Position + 8 > s.Length)
            return false;

        long start = s.Position;
        ReadChunkHeader(reader, out ushort kind, out _, out length);
        s.Position = start;
        return kind == expectedKind;
    }

    private static void ReadChunkHeader(BinaryReader reader, out ushort kind, out ushort checksum, out uint length)
    {
        kind = reader.ReadUInt16();
        checksum = reader.ReadUInt16();
        length = reader.ReadUInt32();
    }

    /// <summary>NAV Object-table "Type" option value to <see cref="NavObjectType"/>.</summary>
    public static NavObjectType MapType(int navType) => navType switch
    {
        1 => NavObjectType.Table,
        2 => NavObjectType.Form,
        3 => NavObjectType.Report,
        4 => NavObjectType.Dataport,
        5 => NavObjectType.Codeunit,
        6 => NavObjectType.XMLport,
        7 => NavObjectType.MenuSuite,
        8 => NavObjectType.Page,
        9 => NavObjectType.Query,
        _ => NavObjectType.Unknown,
    };

    /// <summary>
    /// An Object-table record: a sequence of [u32 field no.][value], integers as u32,
    /// text as CP850 zero-terminated and padded to a multiple of 4 bytes.
    /// </summary>
    private sealed class ObjectRecord
    {
        private static readonly HashSet<int> TextFields = [2, 4, 12, 14];
        private static readonly HashSet<int> IntFields = [1, 3, 5, 6, 7, 8, 9, 10, 11, 13];

        public int Type { get; private set; }
        public int Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string VersionList { get; private set; } = string.Empty;
        public bool Modified { get; private set; }
        public bool Compiled { get; private set; }
        public int RawDate { get; private set; }
        public int RawTime { get; private set; }

        public static ObjectRecord Parse(byte[] rec)
        {
            var r = new ObjectRecord();
            int pos = 0;
            while (pos + 8 <= rec.Length)
            {
                int field = BitConverter.ToInt32(rec, pos);
                pos += 4;
                if (TextFields.Contains(field))
                {
                    int end = Array.IndexOf(rec, (byte)0, pos);
                    if (end < 0)
                        end = rec.Length;
                    string text = Encodings.Nav.GetString(rec, pos, end - pos);
                    pos = (end + 4) & ~3;
                    if (field == 4)
                        r.Name = text;
                    else if (field == 12)
                        r.VersionList = text;
                    continue;
                }

                if (!IntFields.Contains(field))
                    break;

                int value = BitConverter.ToInt32(rec, pos);
                pos += 4;
                switch (field)
                {
                    case 1: r.Type = value; break;
                    case 3: r.Id = value; break;
                    case 5: r.Modified = value != 0; break;
                    case 6: r.Compiled = value != 0; break;
                    case 10: r.RawDate = value; break;
                    case 11: r.RawTime = value; break;
                }
            }
            return r;
        }

        // NAV stores a date as 2 * (days since 31.12.0000 + 365) + 1 and a time as ms since midnight + 1.
        public string FormatDate()
        {
            if (RawDate <= 1)
                return string.Empty;
            int dayNumber = (RawDate - 1) / 2 - 366;
            return dayNumber is >= 0 and <= 3652058 ? DateOnly.FromDayNumber(dayNumber).ToString("dd.MM.yy") : string.Empty;
        }

        public string FormatTime()
            => RawTime <= 0 ? string.Empty : TimeSpan.FromMilliseconds(RawTime - 1).ToString(@"hh\:mm\:ss");
    }
}
