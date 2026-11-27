using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Services;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Text;
using ProfileKit.Windows;

namespace ProfileKit.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        LanguageBox.SelectedIndex = Localizer.Language == "de" ? 0 : 1;
        var s = AiSettings.Load(Data.AiSettingsPath);
        ProviderBox.SelectedItem = s.Provider;
        if (ProviderBox.SelectedIndex < 0) ProviderBox.SelectedIndex = 0;
        OllamaUrlBox.Text = s.OllamaUrl ?? string.Empty;
        OllamaModelBox.Text = s.OllamaModel ?? string.Empty;
        OpenRouterModelBox.Text = s.OpenRouterModel ?? string.Empty;
        MaxStepsBox.Value = s.MaxSteps;
        ConsentSwitch.IsOn = s.OpenRouterConsent;
        UpdateKeyState();
    }

    private void UpdateKeyState()
    {
        var hasKey = CredentialStore.Read(CredentialStore.OpenRouterTarget) is { Length: > 0 };
        KeyState.Text = Localizer.T(hasKey ? "ui-settings-key-stored" : "ui-settings-key-none");
    }

    private async void Language_Click(object sender, RoutedEventArgs e)
    {
        var lang = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "de";
        (UiSettings.Load() with { Language = lang }).Save();
        if (await App.Window!.ConfirmAsync(Localizer.T("ui-settings-language"), Localizer.T("ui-settings-restart"), Localizer.T("ui-settings-restart-now")))
        {
            Elevation.Restart();
            App.Window.Close();
        }
    }

    private async void SaveAi_Click(object sender, RoutedEventArgs e)
    {
        static string? N(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var s = AiSettings.Load(Data.AiSettingsPath) with
        {
            Provider = ProviderBox.SelectedItem as string ?? AiProviders.Auto,
            OllamaUrl = N(OllamaUrlBox.Text),
            OllamaModel = N(OllamaModelBox.Text),
            OpenRouterModel = N(OpenRouterModelBox.Text),
            MaxSteps = double.IsNaN(MaxStepsBox.Value) ? 30 : Math.Clamp((int)MaxStepsBox.Value, 5, 100),
            OpenRouterConsent = ConsentSwitch.IsOn,
        };
        s.Save(Data.AiSettingsPath);
        await App.Window!.ShowMessageAsync(Localizer.T("ui-nav-settings"), Localizer.T("ui-saved"));
    }

    private async void ModelsOllama_Click(object sender, RoutedEventArgs e) =>
        await App.Window!.RunCliAsync(["ai", "models", "--provider", "ollama"]);

    private async void ModelsOpenRouter_Click(object sender, RoutedEventArgs e) =>
        await App.Window!.RunCliAsync(["ai", "models", "--provider", "openrouter"]);

    private async void Status_Click(object sender, RoutedEventArgs e) => await App.Window!.RunCliAsync(["ai", "status"]);

    private async void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Password)) return;
        await App.Window!.RunCliAsync(["ai", "key", "set", "--stdin"], KeyBox.Password.Trim());
        KeyBox.Password = string.Empty;
        UpdateKeyState();
    }

    private async void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        await App.Window!.RunCliAsync(["ai", "key", "remove"]);
        UpdateKeyState();
    }

    private async void McpInstall_Click(object sender, RoutedEventArgs e) => await App.Window!.RunCliAsync(["mcp", "install"]);
    private async void McpUninstall_Click(object sender, RoutedEventArgs e) => await App.Window!.RunCliAsync(["mcp", "install", "--uninstall"]);
    private async void McpConfig_Click(object sender, RoutedEventArgs e) => await App.Window!.RunCliAsync(["mcp", "config"]);
}
