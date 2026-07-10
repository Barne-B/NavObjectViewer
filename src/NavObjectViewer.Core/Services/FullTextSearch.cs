using System.Text.RegularExpressions;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.Core.Services;

public sealed record SearchMatch(NavObject Object, int Line, string Text);

/// <summary>Per-object summary of a content search: how many lines matched, plus a preview of the first.</summary>
public sealed record ObjectHit(NavObject Object, int Count, int FirstLine, string FirstLineText);

/// <summary>
/// Searches inside object bodies. Reads only the byte ranges of the objects in
/// scope (so a filtered/custom-range search touches a fraction of the file),
/// grouping by source file to reuse a single stream per file.
/// </summary>
public sealed class FullTextSearch
{
    /// <summary>
    /// Uncapped, summarized search: one <see cref="ObjectHit"/> per object that
    /// contains the query (with total match count and a first-line preview). The
    /// result is naturally bounded by the number of objects, not lines, so no cap
    /// is needed. Use <see cref="MatchesInObject"/> to drill into a single object.
    /// </summary>
    public List<ObjectHit> SearchObjects(IEnumerable<NavObject> scope, string query,
        bool ignoreCase, bool useRegex, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var hits = new List<ObjectHit>();
        if (string.IsNullOrEmpty(query))
            return hits;

        Func<string, bool> matches = BuildPredicate(query, ignoreCase, useRegex);
        List<NavObject> objs = scope.OrderBy(o => o.SourceFile).ThenBy(o => o.StartOffset).ToList();
        int done = 0;

        foreach (IGrouping<string, NavObject> group in objs.GroupBy(o => o.SourceFile))
        {
            using var fs = new FileStream(group.Key, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (NavObject obj in group)
            {
                ct.ThrowIfCancellationRequested();
                string text = ReadObjectText(fs, obj);

                int count = 0, firstLine = 0, lineNo = 0;
                string firstText = string.Empty;
                foreach (ReadOnlySpan<char> lineSpan in text.AsSpan().EnumerateLines())
                {
                    lineNo++;
                    string line = lineSpan.ToString();
                    if (!matches(line))
                        continue;

                    count++;
                    if (count == 1)
                    {
                        firstLine = lineNo;
                        firstText = line.Trim();
                    }
                }

                if (count > 0)
                    hits.Add(new ObjectHit(obj, count, firstLine, firstText));

                done++;
                if ((done & 0x3F) == 0)
                    progress?.Report((double)done / objs.Count);
            }
        }

        progress?.Report(1.0);
        return hits;
    }

    /// <summary>All matching lines within a single object (for drill-in). Uncapped.</summary>
    public List<SearchMatch> MatchesInObject(NavObject obj, string query, bool ignoreCase, bool useRegex)
    {
        var matches = new List<SearchMatch>();
        if (string.IsNullOrEmpty(query))
            return matches;

        Func<string, bool> predicate = BuildPredicate(query, ignoreCase, useRegex);
        string text = ObjectReader.ReadText(obj);

        int lineNo = 0;
        foreach (ReadOnlySpan<char> lineSpan in text.AsSpan().EnumerateLines())
        {
            lineNo++;
            string line = lineSpan.ToString();
            if (predicate(line))
                matches.Add(new SearchMatch(obj, lineNo, line.TrimEnd()));
        }
        return matches;
    }

    /// <summary>Flat line-level search (kept for programmatic use / tests). Optional cap.</summary>
    public List<SearchMatch> Search(IEnumerable<NavObject> scope, string query,
        bool ignoreCase, bool useRegex, int maxResults = int.MaxValue,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var matches = new List<SearchMatch>();
        if (string.IsNullOrEmpty(query))
            return matches;

        Func<string, bool> predicate = BuildPredicate(query, ignoreCase, useRegex);
        List<NavObject> objs = scope.OrderBy(o => o.SourceFile).ThenBy(o => o.StartOffset).ToList();
        int done = 0;

        foreach (IGrouping<string, NavObject> group in objs.GroupBy(o => o.SourceFile))
        {
            using var fs = new FileStream(group.Key, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (NavObject obj in group)
            {
                ct.ThrowIfCancellationRequested();
                string text = ReadObjectText(fs, obj);

                int lineNo = 0;
                foreach (ReadOnlySpan<char> lineSpan in text.AsSpan().EnumerateLines())
                {
                    lineNo++;
                    string line = lineSpan.ToString();
                    if (!predicate(line))
                        continue;

                    matches.Add(new SearchMatch(obj, lineNo, line.TrimEnd()));
                    if (matches.Count >= maxResults)
                    {
                        progress?.Report(1.0);
                        return matches;
                    }
                }

                done++;
                if ((done & 0x3F) == 0)
                    progress?.Report((double)done / objs.Count);
            }
        }

        progress?.Report(1.0);
        return matches;
    }

    private static Func<string, bool> BuildPredicate(string query, bool ignoreCase, bool useRegex)
    {
        if (useRegex)
        {
            var rx = new Regex(query, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            return rx.IsMatch;
        }

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return line => line.Contains(query, comparison);
    }

    private static string ReadObjectText(FileStream fs, NavObject obj)
    {
        long length = obj.ByteLength;
        if (length <= 0)
            return string.Empty;

        var bytes = new byte[length];
        fs.Seek(obj.StartOffset, SeekOrigin.Begin);
        int total = 0;
        while (total < bytes.Length)
        {
            int n = fs.Read(bytes, total, bytes.Length - total);
            if (n == 0)
                break;
            total += n;
        }
        return Encodings.Nav.GetString(bytes, 0, total);
    }
}
