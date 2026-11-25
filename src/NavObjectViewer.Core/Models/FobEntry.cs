namespace NavObjectViewer.Core.Models;

/// <summary>
/// Byte ranges of one object inside a .fob container. Every range is copied
/// verbatim when writing a new .fob, so the binary object is never re-encoded.
/// </summary>
public sealed class FobEntry
{
    /// <summary>The object's two text lines in the leading object list.</summary>
    public long HeaderOffset { get; set; }
    public int HeaderLength { get; set; }

    /// <summary>The object's index chunks (Object-table record + size info).</summary>
    public long IndexOffset { get; set; }
    public int IndexLength { get; set; }

    /// <summary>The runtime-metadata entry (generated C# + C/AL code streams); length 0 when absent.</summary>
    public long TrailerOffset { get; set; }
    public int TrailerLength { get; set; }

    public bool Compiled { get; set; }
}
