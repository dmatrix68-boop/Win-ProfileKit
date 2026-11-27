using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Catalog;

namespace ProfileKit.App.Services;

/// <summary>Lesender Zugriff auf Katalog, Vorschläge und KI-Einstellungen – dieselben Orte wie die CLI.</summary>
public static class Data
{
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string UserRulesDirectory => Path.Combine(LocalAppData, "Win-ProfileKit", "rules");
    public static string AiSettingsPath => AiSettings.PathFor(LocalAppData);

    public static RuleCatalog LoadCatalog() =>
        RuleCatalog.Load(Path.Combine(AppPaths.Home, "rules"), UserRulesDirectory);

    public static ProposalStore Proposals => new(ProposalStore.DefaultDirectory(LocalAppData));

    public static int UserRuleCount() =>
        Directory.Exists(UserRulesDirectory) ? Directory.GetFiles(UserRulesDirectory, "*.json").Length : 0;
}

/// <summary>Auswählbarer Eintrag (Modul, Vorschlag) für Listen mit Kontrollkästchen.</summary>
public sealed class SelectableItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
