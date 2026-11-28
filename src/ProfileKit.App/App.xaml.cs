using Microsoft.UI.Xaml;
using ProfileKit.App.Services;
using ProfileKit.Core.Text;

namespace ProfileKit.App;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        // Sprache vor dem ersten XAML setzen – alle {l:Loc}-Texte werden beim Laden aufgelöst
        Localizer.SetLanguage(UiSettings.Load().Language);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        // --page <capture|apply|learn|ai|settings>: direkt eine Seite öffnen (auch für den Starttest in der CI)
        var cmd = Environment.GetCommandLineArgs();
        var i = Array.FindIndex(cmd, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < cmd.Length) Window.NavigateTo(cmd[i + 1]);
        Window.Activate();
    }
}
