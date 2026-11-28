using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using global::Windows.Graphics;
using Microsoft.UI.Xaml.Controls;
using ProfileKit.App.Pages;
using ProfileKit.App.Services;
using ProfileKit.Core.Text;

namespace ProfileKit.App;

public sealed partial class MainWindow : Window
{
    private readonly CliRunner _runner = new();
    private readonly StringBuilder _log = new();
    private RectInt32? _normalBounds;

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        var icon = Path.Combine(AppContext.BaseDirectory, "profilekit.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        AppWindow.Changed += AppWindow_Changed;
        Closed += (_, _) => SavePlacement();
        AdminBar.IsOpen = !Elevation.IsAdministrator();
        _runner.Line += (line, isError) => DispatcherQueue.TryEnqueue(() => AppendLog(isError ? "! " + line : line));
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    // ------------------------------------------------------ Fensterlage

    private const int DefaultWidth = 1180, DefaultHeight = 860, MinWidth = 640, MinHeight = 480;

    /// <summary>Stellt Position und Größe vom letzten Mal wieder her – nur, wenn sie noch auf einem Bildschirm liegen.</summary>
    private void RestorePlacement()
    {
        var saved = UiSettings.Load().Window;
        if (saved is { Width: >= MinWidth, Height: >= MinHeight })
        {
            var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
            // Titelleiste muss sichtbar sein (Bildschirm abgesteckt, Auflösung geändert …)
            var titleBar = new RectInt32(saved.X + 40, saved.Y, Math.Max(1, saved.Width - 80), 32);
            if (DisplayArea.GetFromRect(titleBar, DisplayAreaFallback.None) is not null)
            {
                AppWindow.MoveAndResize(rect);
                _normalBounds = rect;
                if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
                return;
            }
        }

        // Erster Start: Standardgröße, aber nie größer als der Arbeitsbereich, und mittig
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(DefaultWidth, area.Width - 32);
        var height = Math.Min(DefaultHeight, area.Height - 32);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange) return;
        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
            _normalBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
    }

    private void SavePlacement()
    {
        try
        {
            var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            if (_normalBounds is not { } b) return;
            (UiSettings.Load() with { Window = new WindowPlacement(b.X, b.Y, b.Width, b.Height, maximized) }).Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    public bool IsAdmin { get; } = Elevation.IsAdministrator();

    public bool IsBusy => _runner.IsRunning;

    /// <summary>Führt die CLI aus; Ausgabe landet im Protokoll. Liefert den Exitcode oder null, wenn schon etwas läuft.</summary>
    public async Task<int?> RunCliAsync(IEnumerable<string> args, string? stdin = null)
    {
        if (_runner.IsRunning)
        {
            await ShowMessageAsync(Localizer.T("ui-busy-title"), Localizer.T("ui-busy"));
            return null;
        }
        var all = args.ToList();
        all.Add("--lang");
        all.Add(Localizer.Language);

        LogExpander.IsExpanded = true;
        BusyRing.IsActive = true;
        CancelButton.IsEnabled = true;
        StatusText.Text = Localizer.T("ui-running");
        try
        {
            var code = await _runner.RunAsync(all, stdin);
            StatusText.Text = Localizer.T("ui-done", code);
            AppendLog(Localizer.T("ui-done", code));
            AppendLog(string.Empty);
            return code;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppendLog("! " + ex.Message);
            StatusText.Text = ex.Message;
            return -1;
        }
        finally
        {
            BusyRing.IsActive = false;
            CancelButton.IsEnabled = false;
        }
    }

    public void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > 400_000) _log.Remove(0, _log.Length - 300_000);
        LogText.Text = _log.ToString();
        LogScroll.UpdateLayout();
        LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null, disableAnimation: true);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Localizer.T("ui-ok"),
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirm)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirm,
            CloseButtonText = Localizer.T("ui-cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public void NavigateTo(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item is not null) Nav.SelectedItem = item;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag }) return;
        var page = tag switch
        {
            "capture" => typeof(CapturePage),
            "apply" => typeof(ApplyPage),
            "learn" => typeof(LearnPage),
            "ai" => typeof(AiPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(OverviewPage),
        };
        ContentFrame.Navigate(page);
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (Elevation.RestartElevated()) Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _runner.Cancel();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        LogText.Text = string.Empty;
    }
}
