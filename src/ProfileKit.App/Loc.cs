using Microsoft.UI.Xaml.Markup;
using ProfileKit.Core.Text;

namespace ProfileKit.App;

/// <summary>
/// Zweisprachige Texte in XAML über den gemeinsamen <see cref="Localizer"/>:
/// <c>Text="{l:Loc Key=ui-nav-capture}"</c>. Die Sprache wird beim Start gesetzt.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Loc : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Localizer.T(Key);
}
