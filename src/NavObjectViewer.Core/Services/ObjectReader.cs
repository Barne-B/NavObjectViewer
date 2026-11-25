using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Reads an object's bytes from its source file on demand, using the byte range
/// recorded in the index. Raw bytes are copied verbatim for export (guaranteed
/// round-trip); text is decoded as CP850 for display.
/// </summary>
public static class ObjectReader
{
    /// <summary>Read the exact source bytes for an object (for byte-faithful export).</summary>
    public static byte[] ReadBytes(NavObject obj)
    {
        long length = obj.ByteLength;
        if (length <= 0)
            return [];

        var bytes = new byte[length];
        using var fs = new FileStream(obj.SourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(obj.StartOffset, SeekOrigin.Begin);
        int total = 0;
        while (total < bytes.Length)
        {
            int n = fs.Read(bytes, total, bytes.Length - total);
            if (n == 0)
                break;
            total += n;
        }
        return total == bytes.Length ? bytes : bytes[..total];
    }

    /// <summary>
    /// Read an object's text for viewing: CP850-decoded source for text exports,
    /// or the decoded C/AL view (<see cref="FobText"/>) for .fob objects.
    /// </summary>
    public static string ReadText(NavObject obj)
        => obj.IsFob ? FobText.Build(obj) : Encodings.Nav.GetString(ReadBytes(obj));
}
