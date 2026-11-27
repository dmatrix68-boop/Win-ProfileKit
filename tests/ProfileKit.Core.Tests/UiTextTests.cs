using System.Text.RegularExpressions;
using ProfileKit.Core.Text;

namespace ProfileKit.Core.Tests;

public class UiTextTests
{
    /// <summary>Quellordner der WinUI-Oberfläche relativ zum Testausgabeordner suchen.</summary>
    private static string? AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ProfileKit.App"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "src", "ProfileKit.App");
    }

    [Fact]
    public void Every_text_key_used_by_the_interface_exists_in_both_languages()
    {
        var source = AppSource();
        Assert.NotNull(source);
        var pattern = new Regex(@"Key=(?<k>[a-z0-9-]+)|""(?<k>(ui|ai|error|warning)-?[a-z0-9-]*)""", RegexOptions.CultureInvariant);
        var keys = Directory.EnumerateFiles(source!, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => pattern.Matches(File.ReadAllText(f)).Select(m => m.Groups["k"].Value))
            .Where(k => k.StartsWith("ui-") || k is "ai-cloud-warning" or "error")
            .Distinct()
            .ToList();

        Assert.True(keys.Count > 100, $"nur {keys.Count} Schlüssel gefunden – Suchmuster prüfen");
        var missingDe = keys.Where(k => !Localizer.German.ContainsKey(k)).ToList();
        var missingEn = keys.Where(k => !Localizer.English.ContainsKey(k)).ToList();
        Assert.True(missingDe.Count == 0, "fehlt (de): " + string.Join(", ", missingDe));
        Assert.True(missingEn.Count == 0, "fehlt (en): " + string.Join(", ", missingEn));
    }
}
