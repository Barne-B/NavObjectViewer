using System.Text;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Builds a C/AL-style text view of an object read from a .fob. Object properties
/// come from the Object-table record; triggers and functions come from the C/AL
/// code stream. Variable declarations, IDs, fields, controls and other properties
/// live only in the undocumented compiled BLOB, so the result is for reading,
/// searching and diffing — it cannot be imported into C/SIDE.
/// </summary>
public static class FobText
{
    public static string Build(NavObject obj)
    {
        string? code;
        string? codeError = null;
        try
        {
            code = FobReader.ReadCode(obj);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            code = null;
            codeError = ex.Message;
        }

        var sb = new StringBuilder();
        sb.Append("OBJECT ").Append(obj.TypeName).Append(' ').Append(obj.Id).Append(' ').AppendLine(obj.Name);
        sb.AppendLine("{");
        sb.AppendLine("  OBJECT-PROPERTIES");
        sb.AppendLine("  {");
        if (obj.Date.Length > 0)
            sb.Append("    Date=").Append(obj.Date).AppendLine(";");
        if (obj.Time.Length > 0)
            sb.Append("    Time=").Append(obj.Time).AppendLine(";");
        if (obj.Modified)
            sb.AppendLine("    Modified=Yes;");
        sb.Append("    Version List=").Append(obj.VersionList).AppendLine(";");
        sb.AppendLine("  }");
        sb.AppendLine("  CODE");
        sb.AppendLine("  {");
        sb.AppendLine("    // Decoded from a binary .fob: triggers and functions with their code.");
        sb.AppendLine("    // Declarations, IDs, fields, controls and properties are not included,");
        sb.AppendLine("    // so this text is for reading and cannot be imported into C/SIDE.");

        if (code is null)
        {
            sb.AppendLine(codeError is null
                ? "    // This object has no stored C/AL code (not compiled, or a pre-2013 .fob)."
                : $"    // The code stream could not be read: {codeError}");
        }
        else
        {
            AppendCode(sb, code);
        }

        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // The stream holds "Signature" lines at column 0, each followed by its body indented by 2.
    private static void AppendCode(StringBuilder sb, string code)
    {
        string? signature = null;
        var body = new List<string>();

        void Flush()
        {
            if (signature is null)
                return;

            while (body.Count > 0 && string.IsNullOrWhiteSpace(body[^1]))
                body.RemoveAt(body.Count - 1);

            sb.AppendLine();
            sb.Append("    ").AppendLine(signature);
            int start = 0;
            while (start < body.Count && body[start].TrimStart().StartsWith("// [", StringComparison.Ordinal))
                sb.Append("    ").AppendLine(body[start++].Trim());

            sb.AppendLine("    BEGIN");
            for (int i = start; i < body.Count; i++)
            {
                string line = body[i].TrimEnd();
                if (line.Length > 0)
                    sb.Append("    ").Append(line);
                sb.AppendLine();
            }
            sb.AppendLine("    END;");
            body.Clear();
        }

        foreach (ReadOnlySpan<char> span in code.AsSpan().EnumerateLines())
        {
            string line = span.ToString();
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                Flush();
                signature = line.TrimEnd();
            }
            else if (signature is not null)
            {
                body.Add(line);
            }
        }
        Flush();
    }
}
