using System.IO;
using System.Linq;
using System.Windows;

namespace NavObjectViewer;

/// <summary>
/// Swaps the active colour palette (dark/light) at runtime by replacing the palette
/// dictionary in the app's merged dictionaries. Controls reference palette brushes via
/// DynamicResource, so every surface updates instantly. The choice is persisted.
/// </summary>
public static class ThemeManager
{
    public enum Mode { Dark, Light }

    public static Mode Current { get; private set; } = Mode.Dark;

    /// <summary>Raised after the theme changes so views can refresh non-XAML resources (e.g. the editor highlighting).</summary>
    public static event Action? Changed;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NavObjectViewer", "theme.txt");

    private static Uri PaletteUri(Mode mode)
        => new($"/Theme/Palette.{(mode == Mode.Light ? "Light" : "Dark")}.xaml", UriKind.Relative);

    /// <summary>Apply the persisted theme at startup.</summary>
    public static void Initialize()
    {
        Current = Load();
        ApplyDictionary(Current);
    }

    public static void Toggle() => Set(Current == Mode.Dark ? Mode.Light : Mode.Dark);

    public static void Set(Mode mode)
    {
        Current = mode;
        ApplyDictionary(mode);
        Save(mode);
        Changed?.Invoke();
    }

    private static void ApplyDictionary(Mode mode)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var current = dicts.FirstOrDefault(d => d.Contains("ThemeName"));
        var next = new ResourceDictionary { Source = PaletteUri(mode) };

        if (current is not null)
            dicts[dicts.IndexOf(current)] = next;
        else
            dicts.Insert(0, next);
    }

    private static Mode Load()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                File.ReadAllText(SettingsPath).Trim().Equals("Light", StringComparison.OrdinalIgnoreCase))
                return Mode.Light;
        }
        catch { /* fall back to dark */ }
        return Mode.Dark;
    }

    private static void Save(Mode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, mode.ToString());
        }
        catch { /* best effort */ }
    }
}
