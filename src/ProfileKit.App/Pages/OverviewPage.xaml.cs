using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Services;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Text;
using ProfileKit.Windows;

namespace ProfileKit.App.Pages;

public sealed partial class OverviewPage : Page
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public OverviewPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        UserText.Text = Localizer.T("ui-overview-user", Environment.UserName, Environment.MachineName);
        AdminText.Text = Localizer.T(Elevation.IsAdministrator() ? "ui-overview-admin-yes" : "ui-overview-admin-no");
        var catalog = Data.LoadCatalog();
        CatalogText.Text = Localizer.T("ui-overview-catalog", catalog.Rules.Count, Data.UserRuleCount());
        ProposalText.Text = Localizer.T("ui-overview-proposals", Data.Proposals.List().Count);

        AiText.Text = Localizer.T("ui-overview-ai", "…");
        var settings = AiSettings.Load(Data.AiSettingsPath);
        var ollama = await AiEnvironment.ProbeOllamaAsync(Http, settings.OllamaUrl, CancellationToken.None);
        var hasKey = await Task.Run(() => CredentialStore.Read(CredentialStore.OpenRouterTarget) is { Length: > 0 });
        var claude = await Task.Run(() => AiEnvironment.FindExecutable("claude"));
        try
        {
            var choice = ProviderSelector.Choose(settings, new AiAvailability { ClaudeCodePath = claude, OllamaModels = ollama, OpenRouterKey = hasKey }, null, null);
            AiText.Text = Localizer.T("ui-overview-ai", choice.Provider + (choice.Model is null ? "" : " · " + choice.Model));
        }
        catch (InvalidOperationException ex)
        {
            AiText.Text = Localizer.T("ui-overview-ai", ex.Message);
        }
    }

    private void GoCapture_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("capture");
    private void GoApply_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("apply");
    private void GoLearn_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("learn");
    private void GoAi_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("ai");
}
