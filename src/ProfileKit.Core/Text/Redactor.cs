using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProfileKit.Core.Model;
using ProfileKit.Core.Paths;

namespace ProfileKit.Core.Text;

/// <summary>
/// Entfernt Zugangsdaten aus Konfigurationsdateien (JSON-Pfade, INI-Schlüssel). Die Kodierung bleibt erhalten.
/// </summary>
public static class Redactor
{
    /// <summary>Gilt eine der Regeln für diese Datei (Pfad relativ zum Dateieintrag)?</summary>
    public static IReadOnlyList<RedactRule> RulesFor(IEnumerable<RedactRule> rules, string relativePath) =>
        rules.Where(r => new Glob(r.File).IsMatch(relativePath)).ToList();

    /// <summary>Liefert die bereinigten Bytes und die Zahl entfernter Einträge; null, wenn nichts zu tun war.</summary>
    public static (byte[] Bytes, int Removed)? Redact(byte[] bytes, IReadOnlyList<RedactRule> rules)
    {
        if (rules.Count == 0 || bytes.Length == 0) return null;
        var detected = TextEncodingDetector.Detect(bytes);
        if (detected is null) return null; // Binärdatei – nicht anfassbar
        var (encoding, preamble) = detected.Value;
        var text = encoding.GetString(bytes, preamble, bytes.Length - preamble);

        var removed = 0;
        foreach (var rule in rules)
        {
            if (rule.Json.Count > 0) text = RedactJson(text, rule.Json, ref removed);
            if (rule.Ini.Count > 0) text = RedactIni(text, rule.Ini, ref removed);
        }
        if (removed == 0) return null;

        var body = encoding.GetBytes(text);
        var output = new byte[preamble + body.Length];
        Array.Copy(bytes, output, preamble);
        Array.Copy(body, 0, output, preamble, body.Length);
        return (output, removed);
    }

    public static string RedactJson(string text, IEnumerable<string> paths, ref int removed)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return text; }
        if (root is null) return text;

        var count = 0;
        foreach (var path in paths)
        {
            // "**.Name" bzw. "**.*Protected": Eigenschaft (Glob) in jeder Tiefe entfernen
            if (path.StartsWith("**.", StringComparison.Ordinal))
            {
                count += RemoveEverywhere(root, new Glob(path[3..]));
                continue;
            }
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            JsonNode? node = root;
            foreach (var part in parts[..^1]) node = node is JsonObject o ? Get(o, part) : null;
            if (node is JsonObject parent && FindKey(parent, parts[^1]) is { } key)
            {
                parent.Remove(key);
                count++;
            }
        }
        if (count == 0) return text;
        removed += count;
        var indented = text.Contains('\n');
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static string RedactIni(string text, IEnumerable<string> keys, ref int removed)
    {
        var targets = keys.Select(k =>
        {
            var dot = k.LastIndexOf('.');
            return dot > 0 ? (Section: k[..dot], Key: k[(dot + 1)..]) : (Section: (string?)null, Key: k);
        }).ToList();

        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n');
        var output = new List<string>(lines.Length);
        string? section = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1].Trim();
            else
            {
                var eq = trimmed.IndexOf('=');
                if (eq > 0 && !trimmed.StartsWith(';') && !trimmed.StartsWith('#'))
                {
                    var key = trimmed[..eq].Trim();
                    if (targets.Any(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                                         && (t.Section is null || t.Section.Equals(section, StringComparison.OrdinalIgnoreCase))))
                    {
                        removed++;
                        continue;
                    }
                }
            }
            output.Add(line);
        }
        return string.Join(newline, output);
    }

    private static int RemoveEverywhere(JsonNode? node, Glob name)
    {
        var count = 0;
        if (node is JsonObject o)
        {
            foreach (var key in o.Select(kv => kv.Key).Where(name.IsMatch).ToList()) { o.Remove(key); count++; }
            foreach (var child in o.Select(kv => kv.Value).ToList()) count += RemoveEverywhere(child, name);
        }
        else if (node is JsonArray a)
        {
            foreach (var child in a) count += RemoveEverywhere(child, name);
        }
        return count;
    }

    private static JsonNode? Get(JsonObject o, string name) => FindKey(o, name) is { } k ? o[k] : null;

    private static string? FindKey(JsonObject o, string name) =>
        o.ContainsKey(name) ? name : o.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
}
