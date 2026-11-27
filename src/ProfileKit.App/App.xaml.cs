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
        Window.Activate();
    }
}
