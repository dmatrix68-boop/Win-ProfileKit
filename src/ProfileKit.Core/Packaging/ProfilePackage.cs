using System.IO.Compression;
using System.Text.RegularExpressions;
using ProfileKit.Core.Model;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Packaging;

/// <summary>
/// Zugriff auf einen Profilordner:
/// <code>
/// profile.json
/// modules/&lt;id&gt;/module.json
/// modules/&lt;id&gt;/registry.json
/// modules/&lt;id&gt;/files/&lt;TOKEN&gt;/…
/// </code>
/// </summary>
public sealed class ProfilePackage
{
    public const string ManifestFileName = "profile.json";
    public const string ModuleFileName = "module.json";
    public const string RegistryFileName = "registry.json";
    public const string FilesFolderName = "files";
    public const string ArchiveExtension = ".pkprofile";

    private static readonly Regex ValidId = new(@"^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    public ProfilePackage(string rootDirectory) => Root = Path.GetFullPath(rootDirectory);

    public string Root { get; }

    public string ManifestPath => Path.Combine(Root, ManifestFileName);

    public bool Exists => File.Exists(ManifestPath);

    public static void ValidateId(string id)
    {
        if (!ValidId.IsMatch(id))
            throw new ArgumentException($"Ungültige Modul-ID '{id}': erlaubt sind a–z, 0–9, '.', '_' und '-' (max. 64 Zeichen).", nameof(id));
    }

    public string ModuleDirectory(string id)
    {
        ValidateId(id);
        return Path.Combine(Root, "modules", id);
    }

    public string FilesDirectory(string id) => Path.Combine(ModuleDirectory(id), FilesFolderName);

    /// <summary>Ablageort einer Datei im Paket zu einem tokenisierten Pfad.</summary>
    public string PackagedPathFor(string moduleId, string tokenizedPath) =>
        PathText.Join(FilesDirectory(moduleId), PathTokens.ToPackagePath(tokenizedPath));

    public ProfileManifest ReadManifest() =>
        Exists ? Json.Read<ProfileManifest>(ManifestPath)
               : throw new FileNotFoundException($"Kein Profil gefunden (fehlt {ManifestFileName}).", ManifestPath);

    public void WriteManifest(ProfileManifest manifest) => Json.Write(ManifestPath, manifest);

    public ModuleRule ReadModule(string id) => Json.Read<ModuleRule>(Path.Combine(ModuleDirectory(id), ModuleFileName));

    public void WriteModule(ModuleRule rule) => Json.Write(Path.Combine(ModuleDirectory(rule.Id), ModuleFileName), rule);

    public IReadOnlyList<RegistryValue> ReadRegistry(string id)
    {
        var path = Path.Combine(ModuleDirectory(id), RegistryFileName);
        return File.Exists(path) ? Json.Read<List<RegistryValue>>(path) : [];
    }

    public void WriteRegistry(string id, IReadOnlyList<RegistryValue> values)
    {
        var path = Path.Combine(ModuleDirectory(id), RegistryFileName);
        if (values.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Json.Write(path, values);
    }

    /// <summary>Packt den Ordner als <c>.pkprofile</c> (ZIP).</summary>
    public void ExportArchive(string archivePath)
    {
        if (!Exists) throw new InvalidOperationException("Profil ist leer – nichts zu exportieren.");
        if (File.Exists(archivePath)) File.Delete(archivePath);
        ZipFile.CreateFromDirectory(Root, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    /// <summary>Entpackt ein <c>.pkprofile</c> in einen Ordner (Schutz gegen Pfade außerhalb des Ziels).</summary>
    public static ProfilePackage ImportArchive(string archivePath, string targetDirectory)
    {
        var root = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var entry in zip.Entries)
        {
            var dest = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('\\', '/')));
            if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Ungültiger Eintrag im Archiv: {entry.FullName}");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
        var package = new ProfilePackage(root);
        if (!package.Exists) throw new InvalidDataException($"Archiv enthält kein {ManifestFileName}.");
        return package;
    }

    /// <summary>Öffnet einen Ordner oder entpackt ein Archiv in einen temporären Ordner.</summary>
    public static ProfilePackage Open(string pathOrArchive)
    {
        if (File.Exists(pathOrArchive) && pathOrArchive.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), "Win-ProfileKit", Path.GetFileNameWithoutExtension(pathOrArchive) + "-" + Guid.NewGuid().ToString("N")[..8]);
            return ImportArchive(pathOrArchive, temp);
        }
        var package = new ProfilePackage(pathOrArchive);
        if (!package.Exists) throw new FileNotFoundException($"Kein Profil unter '{pathOrArchive}' (fehlt {ManifestFileName}).");
        return package;
    }
}
