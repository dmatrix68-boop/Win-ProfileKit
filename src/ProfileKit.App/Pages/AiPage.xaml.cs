using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Services;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Text;

namespace ProfileKit.App.Pages;

public sealed partial class AiPage : Page
{
    private readonly ObservableCollection<SelectableItem> _proposals = [];

    public AiPage()
    {
        InitializeComponent();
        ProposalList.ItemsSource = _proposals;
        Loaded += (_, _) =>
        {
            var settings = AiSettings.Load(Data.AiSettingsPath);
            ProviderBox.SelectedItem = settings.Provider;
            if (ProviderBox.SelectedIndex < 0) ProviderBox.SelectedIndex = 0;
            RefreshProposals();
        };
    }

    private string Provider => ProviderBox.SelectedItem as string ?? AiProviders.Auto;

    private void RefreshProposals()
    {
        _proposals.Clear();
        foreach (var p in Data.Proposals.List())
        {
            var summary = Localizer.T("ui-ai-proposal-line", p.Agent, p.Validation.Paths.Count, p.Validation.Keys.Count, p.Validation.Warnings.Count);
            _proposals.Add(new SelectableItem { Id = p.Rule.Id, Title = $"{p.Rule.DisplayName} ({p.Rule.Id})", Subtitle = summary + "\n" + p.Rule.Notes });
        }
    }

    /// <summary>Gemeinsame Argumente; holt bei OpenRouter ohne gespeicherte Zustimmung die Einwilligung ein.</summary>
    private async Task<List<string>?> CommonArgsAsync()
    {
        var args = new List<string> { "--provider", Provider };
        if (!string.IsNullOrWhiteSpace(ModelBox.Text)) { args.Add("--model"); args.Add(ModelBox.Text.Trim()); }
        if (Provider == AiProviders.OpenRouter && !AiSettings.Load(Data.AiSettingsPath).OpenRouterConsent)
        {
            if (!await App.Window!.ConfirmAsync(Localizer.T("ui-cloud-title"), Localizer.T("ai-cloud-warning"), Localizer.T("ui-cloud-accept")))
                return null;
            args.Add("--accept-cloud");
        }
        return args;
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProgramBox.Text)) { await App.Window!.ShowMessageAsync(Localizer.T("ui-ai-title"), Localizer.T("ui-ai-need-program")); return; }
        if (await CommonArgsAsync() is not { } common) return;
        var args = new List<string> { "ai", "discover", "--app", ProgramBox.Text.Trim() };
        if (!string.IsNullOrWhiteSpace(WingetBox.Text)) { args.Add("--winget"); args.Add(WingetBox.Text.Trim()); }
        args.AddRange(common);
        await App.Window!.RunCliAsync(args);
        RefreshProposals();
    }

    private async void BrowseBatch_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync(App.Window!, ".json") is { } file) BatchFileBox.Text = file;
    }

    private List<string> BatchSource()
    {
        var file = BatchFileBox.Text.Trim();
        if (file.Length == 0) return ["--installed"];
        // OEM-Projekt oder winget-Export am Inhalt erkennen
        var isOem = file.EndsWith(".oemproj.json", StringComparison.OrdinalIgnoreCase)
                    || (File.Exists(file) && File.ReadAllText(file).Contains("\"apps\"", StringComparison.Ordinal));
        return [isOem ? "--from-oemproj" : "--from-winget", file];
    }

    private async void BatchPlan_Click(object sender, RoutedEventArgs e)
    {
        var args = new List<string> { "ai", "discover" };
        args.AddRange(BatchSource());
        args.Add("--dry-run");
        await App.Window!.RunCliAsync(args);
    }

    private async void BatchRun_Click(object sender, RoutedEventArgs e)
    {
        if (await CommonArgsAsync() is not { } common) return;
        var args = new List<string> { "ai", "discover" };
        args.AddRange(BatchSource());
        var limit = LimitBox.Value;
        if (!double.IsNaN(limit) && limit > 0) { args.Add("--limit"); args.Add(((int)limit).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        args.AddRange(common);
        await App.Window!.RunCliAsync(args);
        RefreshProposals();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshProposals();

    private List<string> SelectedIds() => _proposals.Where(p => p.IsSelected).Select(p => p.Id).ToList();

    private async void Show_Click(object sender, RoutedEventArgs e)
    {
        foreach (var id in SelectedIds()) await App.Window!.RunCliAsync(["ai", "proposals", "--show", id]);
    }

    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0) { await App.Window!.ShowMessageAsync(Localizer.T("ui-ai-proposals"), Localizer.T("ui-no-selection")); return; }
        var all = ids.Count == _proposals.Count;
        var targets = all ? new List<string> { "all" } : ids;
        foreach (var id in targets)
        {
            var args = new List<string> { "ai", "accept", id };
            if (ForceBox.IsChecked == true) args.Add("--force");
            if (await App.Window!.RunCliAsync(args) is null) break;
        }
        RefreshProposals();
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0) return;
        if (!await App.Window!.ConfirmAsync(Localizer.T("ui-ai-reject"), Localizer.T("ui-ai-reject-confirm", ids.Count), Localizer.T("ui-ai-reject"))) return;
        foreach (var id in ids)
            if (await App.Window!.RunCliAsync(["ai", "reject", id]) is null) break;
        RefreshProposals();
    }
}
