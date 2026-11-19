using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using QuaiPdf.Core.Pdf;

namespace QuaiPdf.Core.ViewModels;

/// <summary>Une page du document : sa place, sa taille affichée, et la version du contenu à dessiner.</summary>
public sealed partial class PageItemViewModel : ObservableObject
{
    /// <summary>Largeur des vignettes, en pixels indépendants.</summary>
    public const double ThumbnailWidth = 112;

    public PageItemViewModel(int index, PdfPageSize size, double zoom)
    {
        Index = index;
        Size = size;
        Zoom = zoom;
    }

    /// <summary>Position dans le document (à partir de 0).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number), nameof(Label))]
    public partial int Index { get; set; }

    /// <summary>Numéro affiché (à partir de 1).</summary>
    public int Number => Index + 1;

    public string Label => Number.ToString(CultureInfo.CurrentCulture);

    /// <summary>Taille à 100 %, rotation comprise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight), nameof(ThumbnailHeight))]
    public partial PdfPageSize Size { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight))]
    public partial double Zoom { get; set; }

    public double DisplayWidth => Size.Width * Zoom;

    public double DisplayHeight => Size.Height * Zoom;

    public double ThumbnailHeight => Size.Width > 0 ? ThumbnailWidth * Size.Height / Size.Width : ThumbnailWidth;
}
