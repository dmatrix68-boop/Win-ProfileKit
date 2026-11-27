using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;

namespace ProfileKit.App.Services;

/// <summary>
/// Datei-/Ordnerauswahl über Microsoft.Windows.Storage.Pickers (Windows App SDK 1.8) –
/// funktioniert anders als Windows.Storage.Pickers auch, wenn die App als Administrator läuft.
/// </summary>
public static class Pickers
{
    public static async Task<string?> PickFolderAsync(Window window)
    {
        var picker = new FolderPicker(window.AppWindow.Id);
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    public static async Task<string?> PickFileAsync(Window window, params string[] extensions)
    {
        var picker = new FileOpenPicker(window.AppWindow.Id);
        foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }
}
