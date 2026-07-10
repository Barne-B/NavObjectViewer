using System.Text.Json.Serialization;

namespace NavObjectViewer.Core.Models;

/// <summary>
/// A single indexed C/AL object. Holds only lightweight metadata plus the byte
/// range of the object inside its source file; the actual object text is read on
/// demand via <see cref="Services.ObjectReader"/>.
/// </summary>
public sealed class NavObject
{
    public required NavObjectType Type { get; init; }
    public required int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Absolute path of the file this object was indexed from.</summary>
    public required string SourceFile { get; init; }

    /// <summary>Byte offset of the "OBJECT ..." header line (inclusive).</summary>
    public required long StartOffset { get; init; }

    /// <summary>Byte offset one past the last byte of this object (exclusive).</summary>
    public long EndOffset { get; set; }

    // Metadata parsed from the OBJECT-PROPERTIES block.
    public string VersionList { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public bool Modified { get; set; }

    /// <summary>True when the header used the unusual bracketed form: OBJECT [Type Id Name ].</summary>
    public bool IsAnomalous { get; set; }

    [JsonIgnore]
    public long ByteLength => EndOffset - StartOffset;

    /// <summary>Classic filename for per-object export, e.g. "COD80040.txt".</summary>
    [JsonIgnore]
    public string FileName => $"{Type.Prefix()}{Id}.txt";

    [JsonIgnore]
    public string TypeName => Type.ToString();

    public override string ToString() => $"{TypeName} {Id} {Name}";
}
