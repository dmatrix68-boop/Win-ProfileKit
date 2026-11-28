using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Services;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;
using ProfileKit.Core.Text;
using ProfileKit.Windows;

namespace ProfileKit.App.Pages;

public sealed partial class CapturePage : Page
{
    private readonly ObservableCollection<SelectableItem> _modules = [];
    private readonly List<SelectableItem> _all = [];
    private readonly HashSet<string> _withSettings = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string?> _wingetByModule = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ModuleRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private bool _probed;
    private int _probeRun;
    private string? _autoName;

    public CapturePage()
    {
        InitializeComponent();
        ModuleList.ItemsSource = _modules;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        if (_all.Count > 0) return;
        var catalog = Data.LoadCatalog();
        foreach (var rule in catalog.Rules.OrderBy(r => r.IsWindowsModule).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _all.Add(new SelectableItem
            {
                Id = rule.Id,
                Title = rule.DisplayName,
                Subtitle = rule.Id + (rule.WingetId is null ? "" : " · " + rule.WingetId) + (rule.ContainsKeyfiles ? " · Keyfile" : ""),
            });
        }
        _wingetByModule = catalog.Rules.ToDictionary(r => r.Id, r => r.WingetId, StringComparer.OrdinalIgnoreCase);
        _rules = catalog.Rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        ApplyFilter();

        SourceBox.Items.Add(Localizer.T("ui-current-user", Environment.UserName));
        try
        {
            foreach (var p in UserProfiles.List().Where(p => !p.UserName.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)))
                SourceBox.Items.Add(p.UserName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
        SourceBox.SelectedIndex = 0;   // löst die Prüfung der Quelle aus

        var defaultFolder = UiSettings.Load().LastProfileFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Win-ProfileKit");
        FolderBox.Text = Path.Combine(defaultFolder, Environment.UserName);
        NameBox.Text = Environment.UserName;
        _autoName = Environment.UserName;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var m in _modules) m.IsSelected = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var m in _modules) m.IsSelected = false; }
    private void SelectWindows_Click(object sender, RoutedEventArgs e)
    {
        foreach (var m in _modules.Where(m => m.Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase))) m.IsSelected = true;
    }

    private async void SelectInstalled_Click(object sender, RoutedEventArgs e)
    {
        InstalledButton.IsEnabled = false;
        InventoryRing.IsActive = true;
        try
        {
            var installed = await Task.Run(() => new WingetClient().ListInstalled().Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var m in _modules)
                if (_wingetByModule.GetValueOrDefault(m.Id) is { } id && installed.Contains(id)) m.IsSelected = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            await App.Window!.ShowMessageAsync(Localizer.T("ui-error"), ex.Message);
        }
        finally
        {
            InstalledButton.IsEnabled = true;
            InventoryRing.IsActive = false;
        }
    }

    // ------------------------------------------------- Liste je nach Quelle

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Name und Zielordner folgen der Quelle, solange sie nicht von Hand geändert wurden
        var user = SourceBox.SelectedIndex > 0 ? SourceBox.SelectedItem as string : Environment.UserName;
        if (user is not null && _autoName is not null && NameBox.Text == _autoName)
        {
            var folder = FolderBox.Text.Trim().TrimEnd('\\');
            if (Path.GetFileName(folder) == _autoName && Path.GetDirectoryName(folder) is { Length: > 0 } parent)
                FolderBox.Text = Path.Combine(parent, user);
            NameBox.Text = user;
            _autoName = user;
        }
        _ = ProbeAsync();
    }

    private void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();

    private void Vhdx_LostFocus(object sender, RoutedEventArgs e) => _ = ProbeAsync();

    /// <summary>Zeigt alle Regeln oder nur die, zu denen die Quelle Einstellungen hat; Windows-Module immer.</summary>
    private void ApplyFilter()
    {
        var onlyPresent = FilterBox.IsChecked == true && _probed;
        var visible = _all.Where(m => !onlyPresent || m.Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase) || _withSettings.Contains(m.Id)).ToList();
        foreach (var hidden in _all.Except(visible)) hidden.IsSelected = false;   // Ausgeblendetes nicht heimlich erfassen
        _modules.Clear();
        foreach (var m in visible) _modules.Add(m);
    }

    /// <summary>
    /// Prüft für die gewählte Quelle, zu welchen Programmen Einstellungen existieren (nur Existenz von Dateien und
    /// Registry-Schlüsseln). Andere Benutzer: Registry nur mit Adminrechten (Hive wird kurz schreibgeschützt geladen).
    /// </summary>
    private async Task ProbeAsync()
    {
        if (_all.Count == 0) return;
        var run = ++_probeRun;
        var vhdx = !string.IsNullOrWhiteSpace(VhdxBox.Text);
        var userName = SourceBox.SelectedIndex > 0 ? SourceBox.SelectedItem as string : null;

        if (vhdx)
        {
            _probed = false;
            _withSettings.Clear();
            foreach (var m in _all) m.Status = string.Empty;
            ProbeText.Text = Localizer.T("ui-capture-probe-vhdx");
            ApplyFilter();
            return;
        }

        ProbeRing.IsActive = true;
        ProbeText.Text = Localizer.T("ui-capture-probing");
        var rules = _rules.Values.Where(r => !r.IsWindowsModule).ToList();
        (HashSet<string> Found, bool FilesOnly) result;
        try
        {
            result = await Task.Run(() => Probe(rules, userName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                       or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            if (run != _probeRun) return;
            ProbeRing.IsActive = false;
            ProbeText.Text = ex.Message;
            return;
        }
        if (run != _probeRun) return;   // Quelle wurde inzwischen erneut gewechselt

        _withSettings.Clear();
        _withSettings.UnionWith(result.Found);
        _probed = true;
        foreach (var m in _all.Where(m => !m.Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase)))
            m.Status = "  ·  " + Localizer.T(_withSettings.Contains(m.Id) ? "ui-status-found" : "ui-status-none");
        ProbeRing.IsActive = false;
        ProbeText.Text = Localizer.T("ui-capture-probe-status", result.Found.Count, rules.Count)
                         + (result.FilesOnly ? " " + Localizer.T("ui-capture-probe-files-only") : string.Empty);
        ApplyFilter();
    }

    private static (HashSet<string> Found, bool FilesOnly) Probe(IReadOnlyList<ModuleRule> rules, string? userName)
    {
        UserTarget? target = null;
        UserContext context;
        IRegistryBackend? registry = null;
        try
        {
            if (userName is null)
            {
                target = UserTarget.OpenCurrent();
            }
            else
            {
                try { target = UserTarget.OpenUser(userName, writable: false); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { /* ohne Adminrechte: nur Dateien prüfen */ }
            }

            if (target is not null)
            {
                context = target.Context;
                registry = target.Registry;
            }
            else
            {
                var profile = UserProfiles.Find(userName!) ?? throw new ArgumentException(Localizer.T("user-not-found", userName!));
                context = UserProfiles.BuildContext(profile.UserName, profile.ProfilePath, profile.Sid);
            }

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
                if (SettingsProbe.HasSettings(rule, context, registry)) found.Add(rule.Id);
            return (found, registry is null);
        }
        finally
        {
            target?.Dispose();
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync(App.Window!) is { } folder) FolderBox.Text = folder;
    }

    /// <summary>Quelle: aktueller/anderer Benutzer oder – wenn eine VHDX angegeben ist – Benutzer im Image.</summary>
    private void AddSourceArgs(List<string> args)
    {
        if (!string.IsNullOrWhiteSpace(VhdxBox.Text))
        {
            args.Add("--vhdx"); args.Add(VhdxBox.Text.Trim());
            if (!string.IsNullOrWhiteSpace(VhdxUserBox.Text)) { args.Add("--user"); args.Add(VhdxUserBox.Text.Trim()); }
        }
        else if (SourceBox.SelectedIndex > 0 && SourceBox.SelectedItem is string user)
        {
            args.Add("--user"); args.Add(user);
        }
    }

    private async void BrowseVhdx_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync(App.Window!, ".vhdx", ".avhdx", ".vhd") is { } file)
        {
            VhdxBox.Text = file;
            await ProbeAsync();
        }
    }

    private async void VhdxUsers_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VhdxBox.Text)) return;
        await App.Window!.RunCliAsync(["vhdx", "users", "--vhdx", VhdxBox.Text.Trim()]);
    }

    /// <summary>Sammler: alle installierten Programme mit Regel + Windows + Inventar (SPEC 4.6).</summary>
    private async void Collect_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim().TrimEnd('\\');
        var parent = Path.GetDirectoryName(folder);
        var leaf = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) { await App.Window!.ShowMessageAsync(Localizer.T("ui-capture-title"), Localizer.T("ui-no-folder")); return; }

        var args = new List<string> { "collect", "--out", parent, "--name", leaf };
        AddSourceArgs(args);
        if (ZipBox.IsChecked == true) args.Add("--zip");
        (UiSettings.Load() with { LastProfileFolder = parent }).Save();
        await App.Window!.RunCliAsync(args);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var selected = _modules.Where(m => m.IsSelected).Select(m => m.Id).ToList();
        if (selected.Count == 0) { await App.Window!.ShowMessageAsync(Localizer.T("ui-capture-title"), Localizer.T("ui-no-selection")); return; }
        if (string.IsNullOrWhiteSpace(FolderBox.Text)) { await App.Window!.ShowMessageAsync(Localizer.T("ui-capture-title"), Localizer.T("ui-no-folder")); return; }

        var args = new List<string> { "capture", "--out", FolderBox.Text.Trim(), "--name", string.IsNullOrWhiteSpace(NameBox.Text) ? "Profil" : NameBox.Text.Trim(), "--modules", string.Join(',', selected) };
        AddSourceArgs(args);
        if (ZipBox.IsChecked == true) args.Add("--zip");

        var parent = Path.GetDirectoryName(FolderBox.Text.Trim());
        if (!string.IsNullOrEmpty(parent)) (UiSettings.Load() with { LastProfileFolder = parent }).Save();
        await App.Window!.RunCliAsync(args);
    }
}
