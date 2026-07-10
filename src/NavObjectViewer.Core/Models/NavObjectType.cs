namespace NavObjectViewer.Core.Models;

/// <summary>
/// C/SIDE object types that can appear in a NAV C/AL text export.
/// </summary>
public enum NavObjectType
{
    Unknown = 0,
    Table,
    Page,
    Report,
    Codeunit,
    XMLport,
    Query,
    MenuSuite,
    Dataport,
    Form,
}

public static class NavObjectTypes
{
    /// <summary>Classic C/SIDE 3-letter filename prefix for each object type.</summary>
    public static string Prefix(this NavObjectType type) => type switch
    {
        NavObjectType.Table => "TAB",
        NavObjectType.Page => "PAG",
        NavObjectType.Report => "REP",
        NavObjectType.Codeunit => "COD",
        NavObjectType.XMLport => "XML",
        NavObjectType.Query => "QUE",
        NavObjectType.MenuSuite => "MEN",
        NavObjectType.Dataport => "DAP",
        NavObjectType.Form => "FRM",
        _ => "OBJ",
    };

    /// <summary>Parse the type token from an OBJECT header (case-insensitive).</summary>
    public static NavObjectType Parse(string token) => token.ToLowerInvariant() switch
    {
        "table" => NavObjectType.Table,
        "page" => NavObjectType.Page,
        "report" => NavObjectType.Report,
        "codeunit" => NavObjectType.Codeunit,
        "xmlport" => NavObjectType.XMLport,
        "query" => NavObjectType.Query,
        "menusuite" => NavObjectType.MenuSuite,
        "dataport" => NavObjectType.Dataport,
        "form" => NavObjectType.Form,
        _ => NavObjectType.Unknown,
    };
}
