namespace NavObjectViewer.Core.Models;

/// <summary>Where an object read from a .fob container keeps its runtime metadata.</summary>
public sealed class FobEntry
{
    /// <summary>The runtime-metadata entry (generated C# + C/AL code streams); length 0 when absent.</summary>
    public long TrailerOffset { get; set; }
    public int TrailerLength { get; set; }

    public bool Compiled { get; set; }
}
