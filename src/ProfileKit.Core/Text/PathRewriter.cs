using System.Text;
using System.Text.RegularExpressions;
using ProfileKit.Core.Paths;

namespace ProfileKit.Core.Text;

/// <summary>
/// Ersetzt den Profilpfad der Quelle durch den des Ziels – in Texten, Dateien und Registry-Strings.
/// Erkennt die Schreibweisen <c>C:\Users\alt</c>, <c>C:\\Users\\alt</c>, <c>C:/Users/alt</c> und
/// <c>file:///C:/Users/alt</c>, nur an Pfadgrenzen und ohne Rücksicht auf Groß-/Kleinschreibung.
/// </summary>
public sealed class PathRewriter
{
    private readonly List<(Regex Pattern, string Replacement)> _rules = [];

    public PathRewriter(string sourceProfilePath, string targetProfilePath)
    {
        var src = PathText.NormalizeWindows(sourceProfilePath).TrimEnd('\\');
        var dst = PathText.NormalizeWindows(targetProfilePath).TrimEnd('\\');
        IsIdentity = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase);
        if (IsIdentity) return;

        // Reihenfolge: escaped zuerst, damit "\\" nicht als zwei einfache Trenner behandelt wird.
        Add(src.Replace(@"\", @"\\"), dst.Replace(@"\", @"\\"));
        Add(src, dst);
        Add(src.Replace('\\', '/'), dst.Replace('\\', '/'));
    }

    /// <summary>Quelle und Ziel sind gleich – es gibt nichts zu ersetzen.</summary>
    public bool IsIdentity { get; }

    private void Add(string from, string to)
    {
        // Treffer nur, wenn danach ein Trenner, ein Anführungszeichen/Ende o. Ä. folgt –
        // so bleibt C:\Users\alt2 unangetastet, wenn C:\Users\alt ersetzt wird.
        var boundary = @"(?=$|[\\/""'<>\s;,|*?)\]}&=])";
        var pattern = new Regex(Regex.Escape(from) + boundary, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _rules.Add((pattern, to.Replace("$", "$$")));
    }

    public string Rewrite(string text)
    {
        if (IsIdentity || string.IsNullOrEmpty(text)) return text;
        foreach (var (pattern, replacement) in _rules)
            text = pattern.Replace(text, replacement);
        return text;
    }

    /// <summary>
    /// Schreibt eine Datei um, falls sie Text ist. Kodierung (inkl. BOM) bleibt erhalten.
    /// Liefert true, wenn sich der Inhalt geändert hat.
    /// </summary>
    public bool RewriteFile(string path)
    {
        if (IsIdentity) return false;
        var bytes = File.ReadAllBytes(path);
        var result = RewriteBytes(bytes);
        if (result is null) return false;
        File.WriteAllBytes(path, result);
        return true;
    }

    /// <summary>Liefert die umgeschriebenen Bytes oder null, wenn nichts geändert wurde bzw. keine Textdatei vorliegt.</summary>
    public byte[]? RewriteBytes(byte[] bytes)
    {
        if (IsIdentity || bytes.Length == 0) return null;
        var detected = TextEncodingDetector.Detect(bytes);
        if (detected is null) return null;

        var (encoding, preambleLength) = detected.Value;
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var rewritten = Rewrite(text);
        if (ReferenceEquals(text, rewritten) || text == rewritten) return null;

        var body = encoding.GetBytes(rewritten);
        var output = new byte[preambleLength + body.Length];
        Array.Copy(bytes, output, preambleLength);
        Array.Copy(body, 0, output, preambleLength, body.Length);
        return output;
    }
}

/// <summary>Erkennt, ob Bytes Text sind und mit welcher Kodierung.</summary>
public static class TextEncodingDetector
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>
    /// Liefert Kodierung und BOM-Länge, oder null für Binärdaten.
    /// Ohne BOM: UTF-8, wenn gültig; sonst Latin-1 (byte-treu, ASCII-Pfade bleiben korrekt).
    /// </summary>
    public static (Encoding Encoding, int PreambleLength)? Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (StrictUtf8, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2);

        // UTF-16 LE ohne BOM (häufig bei .reg/.ini aus Windows-Tools): viele NUL an ungeraden Stellen.
        if (LooksLikeUtf16Le(bytes))
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 0);

        var probe = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < probe; i++)
            if (bytes[i] == 0) return null; // Binärdatei

        try
        {
            StrictUtf8.GetString(bytes);
            return (StrictUtf8, 0);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1, 0);
        }
    }

    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes.Length % 2 != 0) return false;
        var probe = Math.Min(bytes.Length, 4096);
        int oddZeros = 0, evenZeros = 0, pairs = probe / 2;
        for (var i = 0; i + 1 < probe; i += 2)
        {
            if (bytes[i] == 0) evenZeros++;
            if (bytes[i + 1] == 0) oddZeros++;
        }
        return oddZeros > pairs * 0.4 && evenZeros < pairs * 0.05;
    }
}
