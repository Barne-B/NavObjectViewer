using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NavObjectViewer;

/// <summary>Matches the OS window title bar to the app's current (dark/light) theme.</summary>
public static class NativeTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // 20 on Windows 10 2004+/11; 19 on earlier Windows 10 builds.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    public static void ApplyTitleBarTheme(Window window, bool dark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            int on = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
        }
        catch
        {
            // Cosmetic only; ignore on platforms without the attribute.
        }
    }
}
