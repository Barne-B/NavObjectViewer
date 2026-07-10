using System.Text;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Central access to the codepage-850 (OEM/DOS) encoding used by classic C/SIDE
/// text exports. .NET Core dropped built-in codepages, so the provider must be
/// registered once before <see cref="Encoding.GetEncoding(int)"/> can return CP850.
/// </summary>
public static class Encodings
{
    private static Encoding? _cp850;

    /// <summary>Codepage used by classic finsql.exe text exports.</summary>
    public const int NavCodePage = 850;

    /// <summary>
    /// Register the codepages provider. Safe to call multiple times. Call once at
    /// application startup (and lazily from <see cref="Nav"/> for library/test use).
    /// </summary>
    public static void Register()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The CP850 encoding, registering the provider on first use.</summary>
    public static Encoding Nav
    {
        get
        {
            if (_cp850 is null)
            {
                Register();
                _cp850 = Encoding.GetEncoding(NavCodePage);
            }
            return _cp850;
        }
    }
}
