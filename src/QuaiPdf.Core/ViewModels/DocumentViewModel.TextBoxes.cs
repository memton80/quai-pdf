using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.ViewModels;

/// <summary>Une couleur proposée pour le texte des zones (nom affiché, couleur).</summary>
public sealed record TextColorChoice(string Name, PdfColor Color);

/// <summary>
/// Zones de texte posées sur les pages (annotations, comme dans Firefox, Chrome, Edge ou Acrobat) : outil « Texte »
/// pour en ajouter d'un clic, sélection, modification du texte, de la taille et de la couleur, déplacement, suppression.
/// Chaque changement est une modification du document comme les autres (annuler, rétablir, enregistrer).
/// </summary>
public sealed partial class DocumentViewModel
{
    /// <summary>Tailles proposées, en points.</summary>
    public static IReadOnlyList<double> TextSizes { get; } = [10, 12, 14, 18, 24, 32];

    private readonly Dictionary<int, IReadOnlyList<PdfTextBox>> _textBoxes = [];
    private IReadOnlyList<TextColorChoice>? _textColors;

    /// <summary>Les zones de texte ont changé (contenu, sélection) : la vue redessine leurs contours.</summary>
    public event EventHandler? TextBoxesChanged;

    /// <summary>Couleurs proposées (noms dans la langue de l'interface).</summary>
    public IReadOnlyList<TextColorChoice> TextColors => _textColors ??=
    [
        new(_strings.Get("TextColor_Black"), PdfColor.Black),
        new(_strings.Get("TextColor_Red"), PdfColor.FromBytes(0xC4, 0x2B, 0x1C)),
        new(_strings.Get("TextColor_Blue"), PdfColor.FromBytes(0x00, 0x5F, 0xB8)),
        new(_strings.Get("TextColor_Green"), PdfColor.FromBytes(0x10, 0x7C, 0x10)),
        new(_strings.Get("TextColor_Orange"), PdfColor.FromBytes(0xCA, 0x50, 0x10)),
    ];

    /// <summary>Outil « Texte » : un clic sur une page y pose une zone de texte.</summary>
    [ObservableProperty]
    public partial bool IsTextToolActive { get; set; }

    /// <summary>Taille et couleur des nouvelles zones (et de la zone sélectionnée quand on les change).</summary>
    [ObservableProperty]
    public partial PdfTextStyle TextStyle { get; private set; } = PdfTextStyle.Default;

    /// <summary>Zone de texte sélectionnée (contour affiché, Suppr la supprime, Entrée la modifie).</summary>
    [ObservableProperty]
    public partial PdfTextBox? SelectedTextBox { get; private set; }

    /// <summary>Zones de texte d'une page (du contenu affiché), lues à la première demande.</summary>
    public IReadOnlyList<PdfTextBox> TextBoxes(int page)
    {
        if (!_textBoxes.TryGetValue(page, out var boxes))
        {
            boxes = TextPage(page)?.TextBoxes() ?? [];
            _textBoxes[page] = boxes;
        }

        return boxes;
    }

    /// <summary>Zone de texte sous ce point (celle du dessus si elles se chevauchent), ou <c>null</c>.</summary>
    public PdfTextBox? TextBoxAt(int page, PdfPoint point)
    {
        var boxes = TextBoxes(page);
        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            if (boxes[i].Area.Contains(point))
            {
                return boxes[i];
            }
        }

        return null;
    }

    public void SelectTextBox(PdfTextBox? box)
    {
        if (SelectedTextBox != box)
        {
            SelectedTextBox = box;
        }
    }

    /// <summary>Pose une nouvelle zone de texte (coin haut gauche en pixels à 100 %) ; elle devient la sélection.</summary>
    public async Task<bool> AddTextBoxAsync(int page, PdfPoint topLeft, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || !HasPage(page))
        {
            return false;
        }

        var style = TextStyle;
        var selection = SelectedIndices;
        var before = TextBoxes(page).Select(b => b.Index).DefaultIfEmpty(-1).Max();
        if (!await ApplyAsync(content => PdfTextBoxes.Add(content, page, Clamp(page, topLeft), text, style), _ => selection, scrollToSelection: false))
        {
            return false;
        }

        SelectTextBox(TextBoxes(page).Where(b => b.Index > before).MaxBy(b => b.Index));
        return true;
    }

    /// <summary>
    /// Change le texte (et le style) d'une zone ; un texte vide la supprime, comme dans Firefox. Rien ne change si le
    /// texte et le style sont les mêmes.
    /// </summary>
    public async Task<bool> UpdateTextBoxAsync(PdfTextBox box, string text, PdfTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(text);
        var normalized = PdfTextBoxes.Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return box.CanDelete && await DeleteTextBoxAsync(box);
        }

        if (normalized == box.Text && style == new PdfTextStyle(box.FontSize, box.Color))
        {
            return false;
        }

        return box.CanEditText && await ReplaceAsync(box, new PdfPoint(box.Area.X, box.Area.Y), normalized, style);
    }

    /// <summary>Déplace une zone (nouveau coin haut gauche, pixels à 100 %), sans sortir de la page.</summary>
    public async Task<bool> MoveTextBoxAsync(PdfTextBox box, PdfPoint topLeft)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!box.CanMove)
        {
            return false;
        }

        var target = Clamp(box.Page, topLeft, box.Area);
        if (Math.Abs(target.X - box.Area.X) < 0.5 && Math.Abs(target.Y - box.Area.Y) < 0.5)
        {
            return false;
        }

        return await ReplaceAsync(box, target, box.Text, new PdfTextStyle(box.FontSize, box.Color));
    }

    public async Task<bool> DeleteTextBoxAsync(PdfTextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!box.CanDelete)
        {
            return false;
        }

        var selection = SelectedIndices;
        var deleted = await ApplyAsync(content => PdfTextBoxes.Delete(content, box.Page, box.Index), _ => selection, scrollToSelection: false);
        if (deleted)
        {
            SelectTextBox(null);
        }

        return deleted;
    }

    /// <summary>Nouvelle taille : pour les prochaines zones, et pour la zone sélectionnée.</summary>
    public Task SetTextSizeAsync(double size) => SetTextStyleAsync(TextStyle with { FontSize = Math.Clamp(size, PdfTextStyle.MinFontSize, PdfTextStyle.MaxFontSize) });

    /// <summary>Nouvelle couleur : pour les prochaines zones, et pour la zone sélectionnée.</summary>
    public Task SetTextColorAsync(PdfColor color) => SetTextStyleAsync(TextStyle with { Color = color });

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedTextBox))]
    private Task DeleteSelectedTextBoxAsync() =>
        SelectedTextBox is { } box ? DeleteTextBoxAsync(box) : Task.CompletedTask;

    private bool CanDeleteSelectedTextBox() => CanEdit && SelectedTextBox is { CanDelete: true };

    private async Task SetTextStyleAsync(PdfTextStyle style)
    {
        TextStyle = style;
        if (SelectedTextBox is { CanEditText: true } box && CanEdit)
        {
            await ReplaceAsync(box, new PdfPoint(box.Area.X, box.Area.Y), box.Text, style);
        }
    }

    /// <summary>Réécrit une zone (texte, style, place) ; elle reste sélectionnée.</summary>
    private async Task<bool> ReplaceAsync(PdfTextBox box, PdfPoint topLeft, string text, PdfTextStyle style)
    {
        var selection = SelectedIndices;
        if (!await ApplyAsync(content => PdfTextBoxes.Update(content, box.Page, box.Index, topLeft, text, style), _ => selection, scrollToSelection: false))
        {
            return false;
        }

        SelectTextBox(TextBoxes(box.Page).FirstOrDefault(b => b.Index == box.Index));
        return true;
    }

    private bool HasPage(int page) => page >= 0 && page < PageCount;

    /// <summary>Coin haut gauche gardé sur la page (une zone ne part jamais hors de la page).</summary>
    private PdfPoint Clamp(int page, PdfPoint topLeft, PdfRect? area = null)
    {
        if (!HasPage(page))
        {
            return topLeft;
        }

        var size = Pages[page].Size;
        var width = area?.Width ?? 0;
        var height = area?.Height ?? 0;
        return new PdfPoint(
            Math.Clamp(topLeft.X, 0, Math.Max(0, size.Width - Math.Min(width, size.Width))),
            Math.Clamp(topLeft.Y, 0, Math.Max(0, size.Height - Math.Min(height, size.Height))));
    }

    // Document modifiable (une opération en cours ne compte pas : l'outil reste choisi pendant qu'elle se fait).
    private bool AcceptsTextBoxes => HasDocument && Editability == PdfEditability.Editable;

    partial void OnIsTextToolActiveChanged(bool value)
    {
        if (value && !AcceptsTextBoxes)
        {
            IsTextToolActive = false;
        }
    }

    partial void OnSelectedTextBoxChanged(PdfTextBox? value)
    {
        DeleteSelectedTextBoxCommand.NotifyCanExecuteChanged();
        TextBoxesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Nouveau contenu : zones relues à la demande, sélection oubliée (une opération la repose).</summary>
    private void ResetTextBoxes()
    {
        _textBoxes.Clear();
        SelectedTextBox = null;
        if (!AcceptsTextBoxes)
        {
            IsTextToolActive = false;
        }

        TextBoxesChanged?.Invoke(this, EventArgs.Empty);
    }
}
