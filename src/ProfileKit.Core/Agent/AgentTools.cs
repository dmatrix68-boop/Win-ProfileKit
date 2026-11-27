using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProfileKit.Core.Catalog;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;
using ProfileKit.Core.Text;
using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Agent;

/// <summary>Ergebnis eines Werkzeugaufrufs (Text für das Modell, meist JSON).</summary>
public sealed record ToolResult(string Text, bool IsError = false)
{
    public static ToolResult Json(object value) => new(JsonSerializer.Serialize(value, Packaging.Json.Options));
    public static ToolResult Error(string message) => new(message, IsError: true);
}

/// <summary>Ein Werkzeug für KI-Agenten – gemeinsam genutzt von MCP-Server und eigenem Agenten-Ablauf.</summary>
public sealed record AgentTool(string Name, string Description, JsonObject InputSchema, Func<JsonObject, ToolResult> Handler)
{
    public ToolResult Invoke(JsonObject? arguments)
    {
        try { return Handler(arguments ?? new JsonObject()); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or FormatException
                                       or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}

/// <summary>Umgebung der Werkzeuge: Benutzer, Registry, Katalog, Vorschlagsablage.</summary>
public sealed class AgentContext
{
    public required UserContext User { get; init; }
    public required IRegistryBackend Registry { get; init; }
    public required RuleCatalog Catalog { get; init; }
    public required ProposalStore Proposals { get; init; }

    /// <summary>Name des Agenten, der Vorschläge macht (z. B. "claude-code", "ollama:qwen3").</summary>
    public string AgentName { get; init; } = "mcp";

    /// <summary>Optional: winget-Inventar (Windows). Null = nur Uninstall-Einträge.</summary>
    public Func<IReadOnlyList<InstalledPackage>>? WingetInventory { get; init; }

    /// <summary>In dieser Sitzung gespeicherte Vorschläge.</summary>
    public List<string> ProposedIds { get; } = [];

    public AccessGuard Guard => _guard ??= new AccessGuard(User);
    private AccessGuard? _guard;
}

/// <summary>Die Werkzeuge. Alle sind lesend – bis auf <c>propose_rule</c>, das nur einen Vorschlag ablegt.</summary>
public static class AgentTools
{
    private const int MaxSearchResults = 100;
    private const int MaxListEntries = 300;
    private const int DefaultReadBytes = 32 * 1024;

    private static readonly string[] UninstallKeys =
    [
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public static IReadOnlyList<AgentTool> Create(AgentContext ctx) =>
    [
        new("list_installed_apps",
            "List installed programs (Windows uninstall entries: name, version, publisher, install location as tokenized path) and, if available, their winget package IDs. Use 'filter' to narrow by name.",
            Schema(("filter", "string", "Optional case-insensitive substring of the program name or publisher.", false)),
            a => ListInstalledApps(ctx, Str(a, "filter"))),

        new("search_paths",
            "Search folder/file names and registry key names containing 'query' in the user profile (AppData, LocalLow, dot-folders), ProgramData, Program Files and in HKCU\\Software / HKLM\\SOFTWARE. Returns tokenized paths such as %APPDATA%\\Vendor\\App.",
            Schema(("query", "string", "Name fragment, at least 3 characters, e.g. the program or vendor name.", true),
                   ("include_registry", "boolean", "Also search registry key names (default true).", false)),
            a => SearchPaths(ctx, Req(a, "query"), Bool(a, "include_registry") ?? true)),

        new("list_directory",
            "List the entries of a folder (name, type, size, last modified). Path may be tokenized (%APPDATA%\\X) or absolute inside the allowed locations.",
            Schema(("path", "string", "Folder path.", true)),
            a => ListDirectory(ctx, Req(a, "path"))),

        new("read_file",
            "Read a text file (configuration). Returns metadata and the text content (truncated). Content of credential, key and license files is withheld.",
            Schema(("path", "string", "File path (tokenized or absolute).", true),
                   ("max_bytes", "integer", $"Maximum bytes to return (default {DefaultReadBytes}, max {AccessGuard.MaxContentBytes}).", false)),
            a => ReadFile(ctx, Req(a, "path"), Int(a, "max_bytes"))),

        new("read_registry",
            "Read the values and subkey names of a registry key below HKCU\\Software, HKCU\\Control Panel or HKLM\\SOFTWARE. Sensitive values (passwords, tokens, licenses) are redacted.",
            Schema(("key", "string", "Key, e.g. HKCU\\Software\\Vendor\\App.", true),
                   ("recurse", "boolean", "Include values of subkeys (max depth 3, default false).", false)),
            a => ReadRegistry(ctx, Req(a, "key"), Bool(a, "recurse") ?? false)),

        new("get_rule",
            "Get an existing catalog rule by module id or winget id, or list all known module ids if neither is given. Check this before proposing a new rule.",
            Schema(("id", "string", "Module id, e.g. notepadplusplus.", false), ("winget_id", "string", "winget package id.", false)),
            a => GetRule(ctx, Str(a, "id"), Str(a, "winget_id"))),

        new("propose_rule",
            "Propose the rule for one program. It is validated against this computer and stored as a proposal for the user to review – it is NOT applied. " +
            "Fix reported errors and call again. Paths must use tokens (%APPDATA%, %LOCALAPPDATA%, %LOCALAPPDATALOW%, %USERPROFILE%, %PROGRAMDATA%, %PROGRAMFILES%, %PROGRAMFILES(X86)%).",
            RuleSchema(),
            a => ProposeRule(ctx, a)),
    ];

    // ----------------------------------------------------------------- tools

    private static ToolResult ListInstalledApps(AgentContext ctx, string? filter)
    {
        var apps = new List<object>();
        foreach (var root in UninstallKeys)
        {
            foreach (var sub in ctx.Registry.GetSubKeyNames(root))
            {
                var key = root + "\\" + sub;
                var values = ctx.Registry.Read(key, ["DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "SystemComponent"], false)
                    .ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
                if (!values.TryGetValue("DisplayName", out var name) || string.IsNullOrWhiteSpace(name.Text)) continue;
                if (values.TryGetValue("SystemComponent", out var sys) && sys.Number == 1) continue;
                var publisher = values.GetValueOrDefault("Publisher")?.Text;
                if (filter is not null && !Contains(name.Text, filter) && !Contains(publisher, filter)) continue;

                var location = values.GetValueOrDefault("InstallLocation")?.Text?.Trim('"');
                apps.Add(new
                {
                    name = name.Text,
                    version = values.GetValueOrDefault("DisplayVersion")?.Text,
                    publisher,
                    installLocation = string.IsNullOrWhiteSpace(location) ? null : PathTokens.TryTokenize(location, ctx.User) ?? location,
                    uninstallKey = key,
                });
                if (apps.Count >= MaxListEntries) break;
            }
        }

        IEnumerable<string>? winget = null;
        try { winget = ctx.WingetInventory?.Invoke().Select(p => p.Id).Where(id => filter is null || Contains(id, filter)).ToList(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException) { }

        return ToolResult.Json(new { apps, wingetIds = winget });
    }

    private static ToolResult SearchPaths(AgentContext ctx, string query, bool includeRegistry)
    {
        if (query.Trim().Length < 3) return ToolResult.Error("query must have at least 3 characters.");
        var files = new List<string>();
        var roots = new (string Token, int Depth)[]
        {
            ("%APPDATA%", 4), ("%LOCALAPPDATA%", 4), ("%LOCALAPPDATALOW%", 3), ("%PROGRAMDATA%", 3),
            ("%PROGRAMFILES%", 2), ("%PROGRAMFILES(X86)%", 2), ("%USERPROFILE%", 1),
        };
        foreach (var (token, depth) in roots)
        {
            var (tokenized, absolute) = ctx.Guard.ResolvePath(token);
            Walk(absolute, tokenized, depth, query, files, token == "%USERPROFILE%");
            if (files.Count >= MaxSearchResults) break;
        }

        var keys = new List<string>();
        if (includeRegistry)
        {
            foreach (var root in new[] { @"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node" })
                WalkRegistry(ctx.Registry, root, 3, query, keys);
        }
        return ToolResult.Json(new { files = files.Distinct().Take(MaxSearchResults), registryKeys = keys.Distinct().Take(MaxSearchResults) });
    }

    private static void Walk(string absolute, string tokenized, int depth, string query, List<string> results, bool dotOnly)
    {
        if (depth <= 0 || results.Count >= MaxSearchResults) return;
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(absolute, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var tokenPath = tokenized + "\\" + name;
            var isDir = Directory.Exists(entry);
            if (dotOnly && isDir && !name.StartsWith('.') && !name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
            if (dotOnly && name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue; // eigene Wurzeln
            if (Contains(name, query)) results.Add(tokenPath + (isDir ? "\\" : ""));
            if (results.Count >= MaxSearchResults) return;
            if (isDir) Walk(entry, tokenPath, depth - 1, query, results, false);
        }
    }

    private static void WalkRegistry(IRegistryBackend registry, string key, int depth, string query, List<string> results)
    {
        if (depth <= 0 || results.Count >= MaxSearchResults) return;
        foreach (var sub in registry.GetSubKeyNames(key))
        {
            var path = key + "\\" + sub;
            if (sub.Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase) || sub.Equals("Classes", StringComparison.OrdinalIgnoreCase)) continue;
            if (Contains(sub, query)) results.Add(path);
            if (results.Count >= MaxSearchResults) return;
            WalkRegistry(registry, path, depth - 1, query, results);
        }
    }

    private static ToolResult ListDirectory(AgentContext ctx, string path)
    {
        var (tokenized, absolute) = ctx.Guard.ResolvePath(path);
        if (!Directory.Exists(absolute)) return ToolResult.Error($"Folder does not exist: {tokenized}");
        var entries = new List<object>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(absolute, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.OrdinalIgnoreCase))
        {
            var isDir = Directory.Exists(entry);
            var info = isDir ? null : new FileInfo(entry);
            entries.Add(new
            {
                name = Path.GetFileName(entry),
                type = isDir ? "dir" : "file",
                size = info?.Length,
                modified = (isDir ? Directory.GetLastWriteTimeUtc(entry) : info!.LastWriteTimeUtc).ToString("yyyy-MM-dd HH:mm"),
            });
            if (entries.Count >= MaxListEntries) break;
        }
        return ToolResult.Json(new { path = tokenized, entries, truncated = entries.Count >= MaxListEntries });
    }

    private static ToolResult ReadFile(AgentContext ctx, string path, int? maxBytes)
    {
        var (tokenized, absolute) = ctx.Guard.ResolvePath(path);
        if (!File.Exists(absolute)) return ToolResult.Error($"File does not exist: {tokenized}");
        var info = new FileInfo(absolute);
        var meta = new { path = tokenized, size = info.Length, modified = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm") };
        if (!AccessGuard.IsContentAllowed(tokenized))
            return ToolResult.Json(new { meta.path, meta.size, meta.modified, content = (string?)null, note = "Content withheld: credential, key or license file. If it is a license key file, include it with kind Keyfile." });

        var limit = Math.Clamp(maxBytes ?? DefaultReadBytes, 1, AccessGuard.MaxContentBytes);
        byte[] bytes;
        using (var stream = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[Math.Min(limit, info.Length)];
            var read = 0;
            while (read < bytes.Length) { var n = stream.Read(bytes, read, bytes.Length - read); if (n == 0) break; read += n; }
        }
        var detected = TextEncodingDetector.Detect(bytes);
        if (detected is null)
            return ToolResult.Json(new { meta.path, meta.size, meta.modified, content = (string?)null, note = "Binary file – content not shown." });
        var (encoding, preamble) = detected.Value;
        var text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
        return ToolResult.Json(new { meta.path, meta.size, meta.modified, encoding = encoding.WebName, truncated = info.Length > bytes.Length, content = text });
    }

    private static ToolResult ReadRegistry(AgentContext ctx, string key, bool recurse)
    {
        var normalized = AccessGuard.NormalizeRegistryKey(key);
        var values = new List<object>();
        CollectValues(ctx.Registry, normalized, recurse ? 3 : 0, values);
        var subkeys = ctx.Registry.GetSubKeyNames(normalized);
        if (values.Count == 0 && subkeys.Count == 0) return ToolResult.Error($"Registry key does not exist or is empty: {normalized}");
        return ToolResult.Json(new { key = normalized, values, subkeys = subkeys.Take(MaxListEntries), truncated = values.Count >= MaxListEntries });
    }

    private static void CollectValues(IRegistryBackend registry, string key, int depth, List<object> output)
    {
        foreach (var v in registry.Read(key, [], recurse: false))
        {
            if (output.Count >= MaxListEntries) return;
            output.Add(new { key = v.Key, name = v.Name, kind = v.Kind.ToString(), data = Describe(v) });
        }
        if (depth <= 0) return;
        foreach (var sub in registry.GetSubKeyNames(key))
            CollectValues(registry, key + "\\" + sub, depth - 1, output);
    }

    private static string Describe(RegistryValue v)
    {
        if (AccessGuard.IsSensitiveValueName(v.Name)) return "[redacted]";
        return v.Kind switch
        {
            RegValueKind.String or RegValueKind.ExpandString => Truncate(v.Text ?? "", 500),
            RegValueKind.MultiString => Truncate(string.Join(" | ", v.Lines ?? []), 500),
            RegValueKind.DWord or RegValueKind.QWord => (v.Number ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => $"{v.GetBytes().Length} bytes",
        };
    }

    private static ToolResult GetRule(AgentContext ctx, string? id, string? wingetId)
    {
        if (id is null && wingetId is null)
            return ToolResult.Json(new { modules = ctx.Catalog.Rules.Select(r => new { r.Id, r.WingetId, r.DisplayName }).OrderBy(r => r.Id) });
        var rule = id is not null ? ctx.Catalog.Find(id) : ctx.Catalog.FindByWingetId(wingetId!);
        return rule is null ? new ToolResult("No rule found.") : ToolResult.Json(rule);
    }

    private static ToolResult ProposeRule(AgentContext ctx, JsonObject args)
    {
        var node = args["rule"] ?? args; // einige Modelle senden die Regel direkt statt verschachtelt
        if (node is JsonValue v && v.TryGetValue<string>(out var str)) node = JsonNode.Parse(str);
        var rule = node.Deserialize<ModuleRule>(Packaging.Json.Options) ?? throw new FormatException("rule is missing.");
        rule = rule with { Origin = RuleOrigin.Ai };

        var validation = ProposalValidator.Validate(rule, ctx.Guard, ctx.Registry);
        if (!validation.IsValid)
            return ToolResult.Json(new { stored = false, errors = validation.Errors, validation.Warnings, hint = "Fix the errors and call propose_rule again." });

        ctx.Proposals.Save(new Proposal { Rule = rule, Agent = ctx.AgentName, Validation = validation });
        if (!ctx.ProposedIds.Contains(rule.Id)) ctx.ProposedIds.Add(rule.Id);
        return ToolResult.Json(new
        {
            stored = true,
            id = rule.Id,
            validation.Warnings,
            paths = validation.Paths,
            keys = validation.Keys,
            next = "Proposal stored for review by the user. If warnings point to real problems, fix them and call propose_rule again; otherwise finish with a short summary.",
        });
    }

    // --------------------------------------------------------------- helpers

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string? Str(JsonObject a, string name) =>
        a[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static string Req(JsonObject a, string name) =>
        Str(a, name) ?? throw new ArgumentException($"Argument '{name}' is required.");

    private static bool? Bool(JsonObject a, string name) =>
        a[name] is JsonValue v ? (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) && bool.TryParse(s, out var p) ? p : null) : null;

    private static int? Int(JsonObject a, string name) =>
        a[name] is JsonValue v ? (v.TryGetValue<int>(out var i) ? i : v.TryGetValue<double>(out var d) ? (int)d : v.TryGetValue<string>(out var s) && int.TryParse(s, out var p) ? p : null) : null;

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] props)
    {
        var properties = new JsonObject();
        foreach (var p in props) properties[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(props.Where(p => p.Required).Select(p => (JsonNode)JsonValue.Create(p.Name)!).ToArray()),
        };
    }

    private static JsonObject RuleSchema()
    {
        var str = (string d) => new JsonObject { ["type"] = "string", ["description"] = d };
        var file = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = str("Tokenized path of a folder or file, e.g. %APPDATA%\\Vendor\\App."),
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Config", "Keyfile"), ["description"] = "Keyfile for license key files only." },
            },
            ["required"] = new JsonArray("path"),
        };
        var reg = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["key"] = str("Registry key, e.g. HKCU\\Software\\Vendor\\App."),
                ["values"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Only these value names; omit for the whole key. Use for shared Windows keys." },
                ["recurse"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include subkeys (default true)." },
            },
            ["required"] = new JsonArray("key"),
        };
        var rule = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = str("Module id: lowercase a-z, 0-9, '.', '_', '-' (e.g. wiztree)."),
                ["displayName"] = str("Program name."),
                ["wingetId"] = str("winget package id, if known."),
                ["files"] = new JsonObject { ["type"] = "array", ["items"] = file },
                ["registry"] = new JsonObject { ["type"] = "array", ["items"] = reg },
                ["excludes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Glob patterns relative to each folder for caches, logs, databases, credentials (e.g. *.log, cache\\**)." },
                ["processes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Process names without .exe that should be closed before applying." },
                ["notes"] = str("Short explanation: what is stored where and how you found out (file names, sources)."),
            },
            ["required"] = new JsonArray("id", "displayName", "notes"),
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["rule"] = rule },
            ["required"] = new JsonArray("rule"),
        };
    }
}
