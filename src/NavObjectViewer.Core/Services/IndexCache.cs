using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

/// <summary>
/// Persists a file's object index to %LOCALAPPDATA%\NavObjectViewer\cache so that
/// re-opening an unchanged export is instant. The cache is keyed by the source
/// file's length and last-write time; any mismatch forces a re-index.
/// </summary>
public static class IndexCache
{
    private const int FormatVersion = 1;

    // NOTE: do NOT ignore default values here — the first object in every export has
    // StartOffset == 0, and omitting it would make deserialization fail the `required`
    // member and silently disable the cache.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public List<NavObject> Objects { get; set; } = [];
    }

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NavObjectViewer", "cache");

    private static string CachePathFor(string sourcePath)
    {
        string full = Path.GetFullPath(sourcePath);
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant()));
        return Path.Combine(CacheDir, Convert.ToHexString(hash) + ".json");
    }

    /// <summary>Return the cached index if it is present and still valid, else null.</summary>
    public static List<NavObject>? TryLoad(string sourcePath)
    {
        try
        {
            string cachePath = CachePathFor(sourcePath);
            if (!File.Exists(cachePath))
                return null;

            var info = new FileInfo(sourcePath);
            using FileStream fs = File.OpenRead(cachePath);
            CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(fs, JsonOptions);
            if (cache is null || cache.Version != FormatVersion)
                return null;
            if (cache.Length != info.Length || cache.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
                return null;

            return cache.Objects;
        }
        catch
        {
            return null; // corrupt / unreadable cache — just re-index
        }
    }

    /// <summary>Write the index to the cache. Failures are swallowed (cache is best-effort).</summary>
    public static void Save(string sourcePath, List<NavObject> objects)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            var cache = new CacheFile
            {
                Version = FormatVersion,
                Length = info.Length,
                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                SourcePath = Path.GetFullPath(sourcePath),
                Objects = objects,
            };

            Directory.CreateDirectory(CacheDir);
            using FileStream fs = File.Create(CachePathFor(sourcePath));
            JsonSerializer.Serialize(fs, cache, JsonOptions);
        }
        catch
        {
            // ignore — cache is an optimization only
        }
    }
}
