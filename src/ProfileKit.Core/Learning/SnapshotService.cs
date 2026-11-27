using System.Security.Cryptography;
using System.Text;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Learning;

/// <summary>Erstellt, speichert und lädt Snapshots für den Lernmodus.</summary>
public sealed class SnapshotService
{
    public Snapshot Take(UserContext user, IRegistryBackend registry, LearnScope scope)
    {
        var snapshot = new Snapshot { UserName = user.UserName, ProfilePath = user.ProfilePath };
        var fileExcludes = scope.FileExcludes();
        var regExcludes = scope.RegistryExcludes();

        foreach (var root in scope.FileRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var absolute = PathTokens.Expand(root, user);
            if (!Directory.Exists(absolute)) continue;
            var dotOnly = scope.UserProfileDotEntriesOnly && root.Equals("%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            ScanDirectory(absolute, root, user, fileExcludes, snapshot.Files, dotOnly, scope.FileRoots);
        }

        foreach (var root in scope.RegistryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var value in registry.Read(root, [], recurse: true))
            {
                if (regExcludes.IsExcluded(value.Key + "\\" + value.Name)) continue;
                if (!snapshot.Registry.TryGetValue(value.Key, out var values))
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    snapshot.Registry[value.Key] = values;
                }
                values[value.Name] = Fingerprint(value);
            }
        }
        return snapshot;
    }

    private static void ScanDirectory(string absoluteRoot, string tokenRoot, UserContext user, ExcludeSet excludes,
        Dictionary<string, FileStamp> files, bool dotEntriesOnly, List<string> allRoots)
    {
        var pending = new Stack<(string Abs, string Token, int Depth)>();
        pending.Push((absoluteRoot, tokenRoot, 0));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = false };

        while (pending.Count > 0)
        {
            var (abs, token, depth) = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(abs, "*", options).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (dotEntriesOnly && depth == 0 && !name.StartsWith('.') && Directory.Exists(entry)) continue;
                var tokenPath = token + "\\" + name;

                if (Directory.Exists(entry))
                {
                    // Andere beobachtete Wurzeln (z. B. AppData unter USERPROFILE) nicht doppelt scannen
                    if (allRoots.Any(r => !r.Equals(tokenRoot, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(PathText.NormalizeWindows(PathTokens.Expand(r, user)), PathText.NormalizeWindows(entry), StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (excludes.IsExcluded(tokenPath + "\\~")) continue; // ganzer Ordner ausgeschlossen
                    pending.Push((entry, tokenPath, depth + 1));
                }
                else
                {
                    if (excludes.IsExcluded(tokenPath)) continue;
                    try
                    {
                        var info = new FileInfo(entry);
                        files[tokenPath] = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    /// <summary>Typ + gekürzter SHA-256 der Daten – erkennt Änderungen, ohne Inhalte zu speichern.</summary>
    public static string Fingerprint(RegistryValue value)
    {
        var data = value.Kind switch
        {
            RegValueKind.String or RegValueKind.ExpandString => value.Text ?? string.Empty,
            RegValueKind.MultiString => string.Join("\0", value.Lines ?? []),
            RegValueKind.DWord or RegValueKind.QWord => (value.Number ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.Base64 ?? string.Empty,
        };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return value.Kind + ":" + Convert.ToHexString(hash, 0, 8);
    }

    public static void Save(string path, Snapshot snapshot) => Json.Write(path, snapshot);

    public static Snapshot Load(string path) => Json.Read<Snapshot>(path);
}
