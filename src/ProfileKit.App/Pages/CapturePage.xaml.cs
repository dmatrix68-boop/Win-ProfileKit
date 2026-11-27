using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Services;
using ProfileKit.Core.Text;
using ProfileKit.Windows;

namespace ProfileKit.App.Pages;

public sealed partial class CapturePage : Page
{
    private readonly ObservableCollection<SelectableItem> _modules = [];
    private Dictionary<string, string?> _wingetByModule = new(StringComparer.OrdinalIgnoreCase);

    public CapturePage()
    {
        InitializeComponent();
        ModuleList.ItemsSource = _modules;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        if (_modules.Count > 0) return;
        var catalog = Data.LoadCatalog();
        foreach (var rule in catalog.Rules.OrderBy(r => r.IsWindowsModule).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _modules.Add(new SelectableItem
            {
                Id = rule.Id,
                Title = rule.DisplayName,
                Subtitle = rule.Id + (rule.WingetId is null ? "" : " · " + rule.WingetId) + (rule.ContainsKeyfiles ? " · Keyfile" : ""),
            });
        }
        _wingetByModule = catalog.Rules.ToDictionary(r => r.Id, r => r.WingetId, StringComparer.OrdinalIgnoreCase);

        SourceBox.Items.Add(Localizer.T("ui-current-user", Environment.UserName));
        try
        {
            foreach (var p in UserProfiles.List().Where(p => !p.UserName.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)))
                SourceBox.Items.Add(p.UserName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
        SourceBox.SelectedIndex = 0;

        var defaultFolder = UiSettings.Load().LastProfileFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Win-ProfileKit");
        FolderBox.Text = Path.Combine(defaultFolder, Environment.UserName);
        NameBox.Text = Environment.UserName;
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
        if (await Pickers.PickFileAsync(App.Window!, ".vhdx", ".avhdx", ".vhd") is { } file) VhdxBox.Text = file;
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
