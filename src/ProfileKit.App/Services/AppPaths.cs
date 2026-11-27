namespace ProfileKit.App.Services;

/// <summary>Ort von <c>profilekit.exe</c> und <c>rules\</c>.</summary>
public static class AppPaths
{
    /// <summary>
    /// Neben der Oberfläche (Build-Ausgabe) oder eine Ebene darüber – in den Release-Paketen liegt die
    /// Oberfläche in <c>app\</c>, CLI und Regelkatalog im Root.
    /// </summary>
    public static string Home { get; } = FindHome();

    private static string FindHome()
    {
        var dir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(dir, "profilekit.exe"))) return dir;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dir));
        return parent is not null && File.Exists(Path.Combine(parent, "profilekit.exe")) ? parent : dir;
    }
}
