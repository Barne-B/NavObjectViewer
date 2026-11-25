using System.IO.Compression;
using System.Text;
using NavObjectViewer.Core.Services;

namespace NavObjectViewer.Tests;

/// <summary>
/// Builds a small .fob that follows the container layout C/SIDE writes (see
/// <see cref="FobReader"/>), with deterministic fake BLOBs. Deletes itself on Dispose.
/// </summary>
public sealed class SyntheticFob : IDisposable
{
    public sealed record Obj(int NavType, string TypeName, int Id, string Name, bool Modified, string? Code);

    public const int TailDelta = 10;

    public static readonly Obj[] DefaultObjects =
    [
        new(1, "Table", 50000, "Test Setup", false, "OnInsert()\r\n  TESTFIELD(\"Primary Key\");\r\n \r\n"),
        new(5, "Codeunit", 50001, "Test Mgt.", true,
            "OnRun()\r\n  DoWork;\r\n \r\nDoWork() : Boolean\r\n  // Explain the work\r\n  MESSAGE('Grüße');\r\n  EXIT(TRUE);\r\n \r\nOnAfterWork(VAR Handled : Boolean)\r\n  // [Integration]\r\n"),
        new(8, "Page", 50002, "Test Setup Card", false, null),
    ];

    public string Path { get; }

    private SyntheticFob(byte[] content)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navtest-" + Guid.NewGuid().ToString("N") + ".fob");
        File.WriteAllBytes(Path, content);
    }

    public static SyntheticFob CreateDefault() => new(Build(DefaultObjects));

    public void Dispose()
    {
        try { File.Delete(Path); } catch { /* best effort */ }
    }

    public static byte[] Build(IReadOnlyList<Obj> objects)
    {
        var header = new MemoryStream();
        var index = new MemoryStream();
        var data = new MemoryStream();
        foreach (Obj o in objects)
        {
            byte[] blob = Enumerable.Range(0, 64 + o.Id % 50).Select(i => (byte)(i * 7 + o.Id)).ToArray();
            byte[] record = Record(o, blob.Length);

            WriteAscii(header, $"{o.TypeName,-10}{o.Id,10} {o.Name,-30}    23.01.25       01:00:00 \r\n");
            WriteAscii(header, $"{blob.Length,20} TEST1.00{new string(' ', 50)}\r\n");

            Chunk(index, 1, record);
            Chunk(index, 4, [.. U32(0), .. U32(4), .. U32((uint)(record.Length + blob.Length)), .. U32(uint.MaxValue)]);
            Chunk(data, 2, record);
            Chunk(data, 3, blob);
        }

        byte[] preambleTemplate = Preamble(0, 0);
        byte[] padding = Enumerable.Repeat((byte)0x1A, 4 - (int)(header.Length % 4)).ToArray();
        long endOffset = header.Length + padding.Length + 8 + preambleTemplate.Length + index.Length + data.Length;

        var file = new MemoryStream();
        header.WriteTo(file);
        file.Write(padding);
        Chunk(file, 0x1B01, Preamble(endOffset + TailDelta, objects.Count));
        index.WriteTo(file);
        data.WriteTo(file);
        Chunk(file, 0x1B02, []);
        Chunk(file, 0x1B03, Enumerable.Range(1, 16).Select(i => (byte)(i * 13)).ToArray());

        List<Obj> withCode = objects.Where(o => o.Code is not null).ToList();
        file.Write([.. U32(0x02EE2B79), .. U32(0x19), .. U32(80000), .. U32((uint)withCode.Count)]);
        foreach (Obj o in withCode)
        {
            file.Write([.. U32((uint)o.NavType), .. U32((uint)o.Id), .. U32(0)]);
            Stream(file, $"public sealed class {o.TypeName}{o.Id} {{ }}");
            Stream(file, o.Code!);
        }
        return file.ToArray();
    }

    private static byte[] Preamble(long tailOffset, long objectCount)
    {
        var p = new MemoryStream();
        Tlv(p, 0x1B0A, U32(0x1A41734B));
        Tlv(p, 0x1B11, [.. Tlv(0x2B0B, [1, 2, 3, 4, 5, 6]), .. Tlv(0x2B0C, [9, 8, 7])]);
        p.Write([0x12, 0x1B, 0x07, 0x00, 0, 0, 0, 0]); // the irregular tag: declares 7, carries 4
        Tlv(p, 0x1B13, U32((uint)tailOffset));
        Tlv(p, 0x1B15, U32(1));
        Tlv(p, 0x1B1C, BitConverter.GetBytes(tailOffset));
        Tlv(p, 0x1B1E, BitConverter.GetBytes(objectCount));
        Tlv(p, 0x1B1F, U32(0));
        p.Write(U32(0));
        return p.ToArray();
    }

    private static byte[] Record(Obj o, int blobSize)
    {
        var r = new MemoryStream();
        Field(r, 1, U32((uint)o.NavType));
        Field(r, 2, Text(""));
        Field(r, 3, U32((uint)o.Id));
        Field(r, 4, Text(o.Name));
        Field(r, 5, U32(o.Modified ? 1u : 0u));
        Field(r, 6, U32(1));
        Field(r, 10, U32(1479279));  // 23.01.25
        Field(r, 11, U32(3600001));  // 01:00:00
        Field(r, 12, Text("TEST1.00"));
        Field(r, 8, U32((uint)blobSize));
        return r.ToArray();
    }

    private static byte[] Text(string s)
    {
        byte[] raw = Encodings.Nav.GetBytes(s);
        var padded = new byte[(raw.Length + 4) & ~3];
        raw.CopyTo(padded, 0);
        return padded;
    }

    private static void Field(Stream s, int no, byte[] value)
    {
        s.Write(U32((uint)no));
        s.Write(value);
    }

    private static void Chunk(Stream s, ushort kind, byte[] payload)
    {
        s.Write(BitConverter.GetBytes(kind));
        s.Write(BitConverter.GetBytes(FobWriter.Checksum(payload)));
        s.Write(U32((uint)payload.Length));
        s.Write(payload);
    }

    private static void Tlv(Stream s, ushort tag, byte[] value) => s.Write(Tlv(tag, value));

    private static byte[] Tlv(ushort tag, byte[] value)
        => [.. BitConverter.GetBytes(tag), .. BitConverter.GetBytes((ushort)value.Length), .. value];

    private static void Stream(Stream s, string text)
    {
        var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(Encoding.UTF8.GetBytes(text));
        s.Write(U32((uint)compressed.Length));
        compressed.WriteTo(s);
    }

    private static byte[] U32(uint v) => BitConverter.GetBytes(v);

    private static void WriteAscii(Stream s, string text) => s.Write(Encodings.Nav.GetBytes(text));
}
