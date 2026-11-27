using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Services;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Text;
using ProfileKit.Windows;

namespace ProfileKit.App.Pages;

public sealed partial class ApplyPage : Page
{
    private readonly ObservableCollection<SelectableItem> _modules = [];
    private bool _initialized;

    public ApplyPage()
    {
        InitializeComponent();
        ModuleList.ItemsSource = _modules;
        Loaded += (_, _) => Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            foreach (var p in UserProfiles.List().Where(p => !p.UserName.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)))
                UserBox.Items.Add(p.UserName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
        if (UserBox.Items.Count > 0) UserBox.SelectedIndex = 0;
        if (UiSettings.Load().LastProfileFolder is { } last) ProfileBox.Text = last;
        UpdateTargetUi();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync(App.Window!) is { } folder) { ProfileBox.Text = folder; LoadProfile(); }
    }

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync(App.Window!, ProfilePackage.ArchiveExtension) is { } file) { ProfileBox.Text = file; LoadProfile(); }
    }

    private void Load_Click(object sender, RoutedEventArgs e) => LoadProfile();

    private void LoadProfile()
    {
        _modules.Clear();
        try
        {
            var package = ProfilePackage.Open(ProfileBox.Text.Trim());
            var manifest = package.ReadManifest();
            ProfileInfo.Text = Localizer.T("ui-apply-info", manifest.Name, manifest.Source.UserName, manifest.Source.Machine,
                manifest.CreatedUtc.LocalDateTime.ToString("g"), manifest.Modules.Count);
            foreach (var m in manifest.Modules)
            {
                var details = $"{m.Id} · {m.FileCount} / {m.RegistryValueCount}";
                if (m.ContainsKeyfiles) details += " · Keyfile";
                if (m.RedactedEntries > 0) details += " · " + Localizer.T("ui-redacted", m.RedactedEntries);
                _modules.Add(new SelectableItem { Id = m.Id, Title = m.DisplayName, Subtitle = details, IsSelected = true });
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or UnauthorizedAccessException)
        {
            ProfileInfo.Text = Localizer.T("error", ex.Message);
        }
    }

    private void Target_Changed(object sender, RoutedEventArgs e) => UpdateTargetUi();

    private void UpdateTargetUi()
    {
        if (UserBox is null) return; // während InitializeComponent
        UserBox.IsEnabled = TargetUser.IsChecked == true;
        NewUserPanel.Visibility = TargetNew.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        var needsAdmin = TargetCurrent.IsChecked != true;
        AdminHint.Visibility = needsAdmin && !Elevation.IsAdministrator() ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_modules.Count == 0) LoadProfile();
        var selected = _modules.Where(m => m.IsSelected).Select(m => m.Id).ToList();
        if (selected.Count == 0) { await App.Window!.ShowMessageAsync(Localizer.T("ui-apply-title"), Localizer.T("ui-no-selection")); return; }

        var args = new List<string> { "apply", "--profile", ProfileBox.Text.Trim() };
        string? stdin = null;
        if (TargetCurrent.IsChecked == true) args.Add("--current-user");
        else if (TargetUser.IsChecked == true)
        {
            if (UserBox.SelectedItem is not string user) { await App.Window!.ShowMessageAsync(Localizer.T("ui-apply-title"), Localizer.T("ui-no-user")); return; }
            args.Add("--user"); args.Add(user);
        }
        else if (TargetDefault.IsChecked == true) args.Add("--default-user");
        else
        {
            if (string.IsNullOrWhiteSpace(NewUserName.Text)) { await App.Window!.ShowMessageAsync(Localizer.T("ui-apply-title"), Localizer.T("ui-no-user")); return; }
            args.Add("--create-user"); args.Add(NewUserName.Text.Trim());
            args.Add("--password-stdin");
            stdin = NewUserPassword.Password;
            if (NewUserAdmin.IsChecked == true) args.Add("--admin");
        }

        if (selected.Count < _modules.Count) { args.Add("--only"); args.Add(string.Join(',', selected)); }
        if (InstallBox.IsChecked == true) args.Add("--install");
        if (BackupBox.IsChecked != true) args.Add("--no-backup");
        if (MachineBox.IsChecked != true) args.Add("--no-machine");
        if (DryRunBox.IsChecked == true) args.Add("--dry-run");

        await App.Window!.RunCliAsync(args, stdin);
        NewUserPassword.Password = string.Empty;
    }
}
