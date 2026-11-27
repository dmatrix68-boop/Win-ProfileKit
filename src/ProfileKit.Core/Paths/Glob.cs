using System.Text;
using System.Text.RegularExpressions;

namespace ProfileKit.Core.Paths;

/// <summary>
/// Einfache Glob-Muster für Ausschlüsse, relativ zum Dateieintrag:
/// <c>*</c> = beliebig innerhalb eines Pfadteils, <c>**</c> = beliebig viele Pfadteile, <c>?</c> = ein Zeichen.
/// Ein Muster ohne Trenner (z. B. <c>*.log</c>) gilt in jeder Tiefe.
/// </summary>
public sealed class Glob
{
    private readonly Regex _regex;

    public Glob(string pattern)
    {
        Pattern = pattern;
        var p = pattern.Replace('/', '\\').Trim('\\');
        if (!p.Contains('\\')) p = @"**\" + p;
        _regex = new Regex("^" + ToRegex(p) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Pattern { get; }

    /// <summary>Prüft einen relativen Pfad (Trenner egal).</summary>
    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath.Replace('/', '\\').Trim('\\'));

    private static string ToRegex(string p)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '*' && i + 1 < p.Length && p[i + 1] == '*')
            {
                var followedBySep = i + 2 < p.Length && p[i + 2] == '\\';
                sb.Append(followedBySep ? @"(?:.*\\)?" : ".*");
                i += followedBySep ? 2 : 1;
            }
            else if (c == '*') sb.Append(@"[^\\]*");
            else if (c == '?') sb.Append(@"[^\\]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.ToString();
    }
}

/// <summary>Sammlung von Ausschlüssen.</summary>
public sealed class ExcludeSet
{
    private readonly List<Glob> _globs;

    public ExcludeSet(IEnumerable<string> patterns) => _globs = patterns.Select(p => new Glob(p)).ToList();

    public bool IsExcluded(string relativePath) => _globs.Any(g => g.IsMatch(relativePath));
}
