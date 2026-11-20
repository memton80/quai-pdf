using System.Globalization;
using Microsoft.UI.Xaml;
using QuaiPdf.App.Localization;

namespace QuaiPdf.App;

/// <summary>Fonctions utilisées depuis le XAML (<c>x:Bind</c>).</summary>
public static class UiHelpers
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Microsoft.UI.Xaml.Controls.InfoBarSeverity WarningIf(bool value) =>
        value ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    /// <summary>Un entier pour une propriété <see cref="double"/> (<c>NumberBox.Value</c>).</summary>
    public static double ToDouble(int value) => value;

    /// <summary>Plus grand numéro de page saisissable : jamais 0, sinon la case ramènerait la page affichée à 0.</summary>
    public static double PageMaximum(int count) => Math.Max(1, count);

    /// <summary>« / 12 » à côté du numéro de page.</summary>
    public static string PageCountText(int count) => Loc.Format("Toolbar_PageCount", count);

    /// <summary>Nom d'accessibilité d'une page (« Page 3 »).</summary>
    public static string PageName(int number) => Loc.Format("Page_Name", number);

    public static string VersionText(string version) => Loc.Format("Settings_Version", version);

    /// <summary>Zoom d'un préréglage du menu (« 1.5 » = 150 %), écrit en notation invariante dans le XAML.</summary>
    public static double ParseZoom(object? tag) =>
        double.TryParse(tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) ? zoom : 1;
}
