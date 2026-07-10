using System.Text.RegularExpressions;

namespace NavObjectViewer;

/// <summary>Case-insensitive name matching. Uses wildcard semantics when the pattern
/// contains <c>*</c> or <c>?</c>, otherwise a plain substring match.</summary>
public static class WildcardMatcher
{
    public static bool IsMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;

        if (pattern.IndexOfAny(['*', '?']) < 0)
            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
    }
}
