using ProfileKit.Core.Paths;

namespace ProfileKit.Core.Tests;

/// <summary>Temporäre Benutzerprofile auf der Platte für Capture/Apply-Tests.</summary>
public sealed class TempRoot : IDisposable
{
    public TempRoot() => Path = Directory.CreateTempSubdirectory("pk-test-").FullName;

    public string Path { get; }

    public UserContext User(string name) => new()
    {
        UserName = name,
        ProfilePath = System.IO.Path.Combine(Path, "Users", name),
        ProgramData = System.IO.Path.Combine(Path, "ProgramData"),
        ProgramFiles = System.IO.Path.Combine(Path, "Program Files"),
        ProgramFilesX86 = System.IO.Path.Combine(Path, "Program Files (x86)"),
    };

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

internal static class Win
{
    public static UserContext User(string name) => new() { UserName = name, ProfilePath = $@"C:\Users\{name}" };
}
