using System.IO;

namespace NavObjectViewer;

/// <summary>Minimal file logger so unhandled failures are recorded instead of vanishing.</summary>
public static class Logging
{
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NavObjectViewer", "logs");

    public static string LogFilePath => Path.Combine(LogDirectory, "error.log");

    public static void Log(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string entry = $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {context} ====\n{ex}\n\n";
            File.AppendAllText(LogFilePath, entry);
        }
        catch
        {
            // Logging must never itself throw.
        }
    }
}
