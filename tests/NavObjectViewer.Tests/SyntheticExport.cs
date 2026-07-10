using System.Text;
using NavObjectViewer.Core.Services;

namespace NavObjectViewer.Tests;

/// <summary>
/// Writes a small, deterministic CP850-encoded C/AL export to a temp file that
/// mirrors the quirks of the real data: German umlauts, a name containing '/',
/// a Modified=Yes object, and the bracketed-header anomaly. Deletes itself on Dispose.
/// </summary>
public sealed class SyntheticExport : IDisposable
{
    public string Path { get; }

    private SyntheticExport(string content)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "navtest-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(Path, Encodings.Nav.GetBytes(content));
    }

    public static SyntheticExport CreateDefault() => new(DefaultContent());

    /// <summary>A file containing a single Table with the given id (for collision tests).</summary>
    public static SyntheticExport CreateSingleTable(int id, string name) => new(Join(
    [
        $"OBJECT Table {id} {name}",
        "{",
        "  OBJECT-PROPERTIES",
        "  {",
        "    Version List=TEST1.0;",
        "  }",
        "  FIELDS",
        "  {",
        $"    {{ 1 ; ; Code ; Code10 }}",
        "  }",
        "}",
    ]));

    public void Dispose()
    {
        try { File.Delete(Path); } catch { /* best effort */ }
    }

    private static string DefaultContent()
    {
        var objects = new List<string[]>
        {
            new[]
            {
                "OBJECT Table 3 Payment Terms",
                "{",
                "  OBJECT-PROPERTIES",
                "  {",
                "    Date=24.03.19;",
                "    Time=12:00:00;",
                "    Version List=NAVW114.00;",
                "  }",
                "  PROPERTIES",
                "  {",
                "    CaptionML=[DEU=Fälligkeit;ENU=Due Date];",
                "  }",
                "  FIELDS",
                "  {",
                "  }",
                "}",
            },
            new[]
            {
                "OBJECT Table 9 Country/Region",
                "{",
                "  OBJECT-PROPERTIES",
                "  {",
                "    Version List=NAVW114.00;",
                "  }",
                "  FIELDS",
                "  {",
                "  }",
                "}",
            },
            new[]
            {
                "OBJECT Codeunit 80040 Buchungsfunktionen Mgt.",
                "{",
                "  OBJECT-PROPERTIES",
                "  {",
                "    Date=06.04.22;",
                "    Time=10:00:00;",
                "    Modified=Yes;",
                "    Version List=HRL14.02.04;",
                "  }",
                "  PROPERTIES",
                "  {",
                "  }",
                "  CODE",
                "  {",
                "    PROCEDURE PostSomething@1();",
                "    BEGIN",
                "    END;",
                "  }",
                "}",
            },
            new[]
            {
                "OBJECT [Report 50014 Reise SR buchen abschließen ]",
                "{",
                "  OBJECT-PROPERTIES",
                "  {",
                "    Modified=Yes;",
                "    Version List=temp;",
                "  }",
                "  PROPERTIES",
                "  {",
                "  }",
                "}",
            },
        };

        var sb = new StringBuilder();
        foreach (string[] obj in objects)
        {
            sb.Append(string.Join("\r\n", obj));
            sb.Append("\r\n\r\n"); // blank-line separator, as in real exports
        }
        return sb.ToString();
    }

    private static string Join(string[] lines) => string.Join("\r\n", lines) + "\r\n\r\n";
}
