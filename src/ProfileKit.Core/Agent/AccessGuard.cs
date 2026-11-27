using System.Text.RegularExpressions;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Agent;

/// <summary>
/// Technische Sicherheitsgrenze für KI-Agenten: nur bekannte Orte, keine Pfade außerhalb,
/// keine Inhalte von Zugangsdaten, Schlüsseln oder Lizenzdateien, keine sensiblen Registry-Werte.
/// </summary>
public sealed class AccessGuard(UserContext user)
{
    public const int MaxContentBytes = 128 * 1024;

    public UserContext User { get; } = user;

    private static readonly string[] FileRoots =
        ["USERPROFILE", "APPDATA", "LOCALAPPDATA", "LOCALAPPDATALOW", "PROGRAMDATA", "PROGRAMFILES", "PROGRAMFILES(X86)"];

    public static readonly IReadOnlyList<string> RegistryRoots = [@"HKCU\Software", @"HKCU\Control Panel", @"HKLM\SOFTWARE"];

    /// <summary>Orte, deren Inhalt nie an eine KI geht (Metadaten schon).</summary>
    private static readonly ExcludeSet SensitiveFiles = new(
    [
        @"%APPDATA%\Microsoft\Protect\**", @"%APPDATA%\Microsoft\Credentials\**", @"%LOCALAPPDATA%\Microsoft\Credentials\**",
        @"%APPDATA%\Microsoft\Crypto\**", @"%APPDATA%\Microsoft\SystemCertificates\**", @"%LOCALAPPDATA%\Microsoft\Vault\**",
        @"%USERPROFILE%\.ssh\**", @"%USERPROFILE%\.gnupg\**", @"%USERPROFILE%\.aws\**", @"%USERPROFILE%\.azure\**",
        @".git-credentials", @".netrc", @"_netrc", @"NTUSER.DAT*", @"UsrClass.dat*",
        @"*.pfx", @"*.p12", @"*.pem", @"*.ppk", @"*.kdbx", @"*.key", @"*.lic", @"*.license", @"license*.*", @"rarreg.*",
        @"Login Data*", @"Cookies*", @"Web Data*", @"logins.json", @"key4.db", @"cert9.db", @"Local State",
        @"wcx_ftp.ini", @"RustDesk.toml", @"*.token", @"credentials*", @"secrets*", @"*.env", @".env*",
    ]);

    private static readonly Regex SensitiveValueName = new(
        "pass(word|wd)?|pwd|secret|token|api[-_]?key|credential|licen[cs]e|serial|regkey|product[-_]?key|private|cookie|auth",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Prüft einen Pfad (tokenisiert oder absolut) und liefert tokenisierten und absoluten Pfad.
    /// Wirft <see cref="UnauthorizedAccessException"/> außerhalb der erlaubten Orte.
    /// </summary>
    public (string Tokenized, string Absolute) ResolvePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("path is empty");
        var trimmed = input.Trim().Trim('"');
        string tokenized;
        if (trimmed.StartsWith('%'))
        {
            var (token, rest) = PathTokens.Parse(trimmed);
            tokenized = rest.Length == 0 ? $"%{token.Name}%" : $"%{token.Name}%\\{rest}";
        }
        else
        {
            if (trimmed.Split('\\', '/').Any(s => s == ".."))
                throw new UnauthorizedAccessException("'..' is not allowed.");
            tokenized = PathTokens.TryTokenize(trimmed, User)
                ?? throw new UnauthorizedAccessException($"Path is outside the allowed locations: {input}. Allowed: {string.Join(", ", FileRoots.Select(r => $"%{r}%"))}.");
        }

        var (tok, _) = PathTokens.Parse(tokenized);
        if (!FileRoots.Contains(tok.Name, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Token %{tok.Name}% is not allowed.");

        var absolute = PathTokens.Expand(tokenized, User);
        var root = Path.GetFullPath(tok.Resolve(User));
        var full = Path.GetFullPath(absolute);
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path escapes its root: {input}");
        return (tokenized, absolute);
    }

    /// <summary>Darf der Inhalt dieser Datei an die KI gehen?</summary>
    public static bool IsContentAllowed(string tokenizedPath) => !SensitiveFiles.IsExcluded(tokenizedPath);

    public static string NormalizeRegistryKey(string key)
    {
        var normalized = RegistryPath.Normalize(key);
        if (!RegistryRoots.Any(r => normalized.Equals(r, StringComparison.OrdinalIgnoreCase)
                                    || normalized.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Registry key is outside the allowed roots ({string.Join(", ", RegistryRoots)}): {key}");
        return normalized;
    }

    public static bool IsSensitiveValueName(string name) => SensitiveValueName.IsMatch(name);
}
