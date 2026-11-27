using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Text;

namespace ProfileKit.App.Pages;

public sealed partial class LearnPage : Page
{
    private string _lastAutoId = string.Empty;

    public LearnPage() => InitializeComponent();

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var args = new List<string> { "learn", "start" };
        if (MachineBox.IsChecked == true) args.Add("--machine");
        if (!string.IsNullOrWhiteSpace(ExcludeBox.Text)) { args.Add("--exclude"); args.Add(ExcludeBox.Text.Trim()); }
        await App.Window!.RunCliAsync(args);
    }

    private async void Diff_Click(object sender, RoutedEventArgs e) => await App.Window!.RunCliAsync(["learn", "diff"]);

    private void Name_Changed(object sender, TextChangedEventArgs e)
    {
        // Modul-ID aus dem Namen vorschlagen, solange sie nicht von Hand geändert wurde
        if (IdBox.Text.Length > 0 && IdBox.Text != _lastAutoId) return;
        _lastAutoId = string.IsNullOrWhiteSpace(NameBox.Text) ? string.Empty : ModuleIds.FromName(NameBox.Text);
        IdBox.Text = _lastAutoId;
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(IdBox.Text)) { await App.Window!.ShowMessageAsync(Localizer.T("ui-learn-title"), Localizer.T("ui-learn-need-id")); return; }
        var args = new List<string> { "learn", "stop", "--id", IdBox.Text.Trim() };
        if (!string.IsNullOrWhiteSpace(NameBox.Text)) { args.Add("--name"); args.Add(NameBox.Text.Trim()); }
        if (!string.IsNullOrWhiteSpace(WingetBox.Text)) { args.Add("--winget"); args.Add(WingetBox.Text.Trim()); }
        if (ForceBox.IsChecked == true) args.Add("--force");
        await App.Window!.RunCliAsync(args);
    }
}
