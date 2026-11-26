using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.ViewModels;

/// <summary>
/// Surlignage du texte, comme au marqueur sur du papier (annotations, comme dans Firefox, Chrome, Edge ou Acrobat) :
/// outil « Surligner » qui colore le texte qu'on balaie, ou texte déjà sélectionné surligné d'un coup ; un clic sur un
/// surlignage le sélectionne (Suppr le retire, une couleur choisie le recolore). Chaque changement est une modification
/// du document comme les autres (annuler, rétablir, enregistrer).
/// </summary>
public sealed partial class DocumentViewModel
{
    private readonly Dictionary<int, IReadOnlyList<PdfHighlight>> _highlights = [];
    private IReadOnlyList<TextColorChoice>? _highlightColors;

    /// <summary>Les surlignages ont changé (contenu, sélection) : la vue redessine le contour du surlignage choisi.</summary>
    public event EventHandler? HighlightsChanged;

    /// <summary>
    /// Couleurs de surligneur : teintes vives et claires, qui laissent le texte lisible (elles se multiplient avec le
    /// papier, comme l'encre fluo).
    /// </summary>
    public IReadOnlyList<TextColorChoice> HighlightColors => _highlightColors ??=
    [
        new(_strings.Get("HighlightColor_Yellow"), PdfColor.FromBytes(0xFF, 0xEE, 0x58)),
        new(_strings.Get("HighlightColor_Green"), PdfColor.FromBytes(0x8C, 0xF0, 0x8C)),
        new(_strings.Get("HighlightColor_Blue"), PdfColor.FromBytes(0x8F, 0xD8, 0xFF)),
        new(_strings.Get("HighlightColor_Pink"), PdfColor.FromBytes(0xFF, 0xA8, 0xD8)),
        new(_strings.Get("HighlightColor_Orange"), PdfColor.FromBytes(0xFF, 0xC2, 0x6E)),
    ];

    /// <summary>Outil « Surligner » : le texte balayé à la souris est surligné dès qu'on relâche le bouton.</summary>
    [ObservableProperty]
    public partial bool IsHighlightToolActive { get; set; }

    /// <summary>Couleur des prochains surlignages (et du surlignage sélectionné quand on la change).</summary>
    [ObservableProperty]
    public partial PdfColor HighlightColor { get; private set; } = PdfColor.FromBytes(0xFF, 0xEE, 0x58);

    /// <summary>Surlignage sélectionné (contour affiché, Suppr le retire).</summary>
    [ObservableProperty]
    public partial PdfHighlight? SelectedHighlight { get; private set; }

    /// <summary>Surlignages d'une page (du contenu affiché), lus à la première demande.</summary>
    public IReadOnlyList<PdfHighlight> Highlights(int page)
    {
        if (!_highlights.TryGetValue(page, out var highlights))
        {
            highlights = TextPage(page)?.Highlights() ?? [];
            _highlights[page] = highlights;
        }

        return highlights;
    }

    /// <summary>Surlignage sous ce point (le dernier posé s'ils se chevauchent), ou <c>null</c>.</summary>
    public PdfHighlight? HighlightAt(int page, PdfPoint point) => Highlights(page).LastOrDefault(h => h.Contains(point));

    public void SelectHighlight(PdfHighlight? highlight)
    {
        if (SelectedHighlight != highlight)
        {
            SelectedHighlight = highlight;
        }
    }

    /// <summary>
    /// Fin d'un appui sur une page (bouton relâché, ou double-clic) : avec l'outil « Surligner », le texte balayé est
    /// surligné ; un simple clic sur un surlignage le sélectionne.
    /// </summary>
    public async Task<bool> CompleteTextSelectionAsync(int page, PdfPoint point)
    {
        if (!HasTextSelection)
        {
            SelectHighlight(HighlightAt(page, point));
            return false;
        }

        return IsHighlightToolActive && CanEdit && await HighlightSelectionAsync();
    }

    /// <summary>Nouvelle couleur : pour les prochains surlignages, et pour le surlignage sélectionné.</summary>
    public async Task SetHighlightColorAsync(PdfColor color)
    {
        HighlightColor = color;
        if (SelectedHighlight is { CanEdit: true } highlight && CanEdit)
        {
            var selection = SelectedIndices;
            if (await ApplyAsync(content => PdfHighlights.SetColor(content, highlight.Page, highlight.Index, color), _ => selection, scrollToSelection: false))
            {
                SelectHighlight(Highlights(highlight.Page).FirstOrDefault(h => h.Index == highlight.Index));
            }
        }
    }

    /// <summary>Surligne le texte sélectionné (menu, barre d'outils).</summary>
    [RelayCommand(CanExecute = nameof(CanHighlightSelection))]
    private Task HighlightSelectedTextAsync() => HighlightSelectionAsync();

    /// <summary>Surligne le texte sélectionné (un surlignage par page), dans la couleur choisie.</summary>
    public async Task<bool> HighlightSelectionAsync()
    {
        if (TextSelection is not { IsEmpty: false } selection || !CanEdit)
        {
            return false;
        }

        var spans = new List<PdfHighlightSpan>();
        for (var page = selection.Start.Page; page <= selection.End.Page; page++)
        {
            if (TextSelectionRects(page) is { Count: > 0 } rects)
            {
                spans.Add(new PdfHighlightSpan(page, rects));
            }
        }

        if (spans.Count == 0)
        {
            return false;
        }

        var color = HighlightColor;
        var pages = SelectedIndices;
        return await ApplyAsync(content => PdfHighlights.Add(content, spans, color), _ => pages, scrollToSelection: false);
    }

    private bool CanHighlightSelection() => CanEdit && HasTextSelection;

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedHighlight))]
    private async Task DeleteSelectedHighlightAsync()
    {
        if (SelectedHighlight is not { CanEdit: true } highlight)
        {
            return;
        }

        var selection = SelectedIndices;
        if (await ApplyAsync(content => PdfHighlights.Delete(content, highlight.Page, highlight.Index), _ => selection, scrollToSelection: false))
        {
            SelectHighlight(null);
        }
    }

    private bool CanDeleteSelectedHighlight() => CanEdit && SelectedHighlight is { CanEdit: true };

    partial void OnIsHighlightToolActiveChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        if (!AcceptsTextBoxes)
        {
            IsHighlightToolActive = false;
            return;
        }

        // Un seul outil à la fois ; du texte déjà sélectionné est surligné tout de suite (comme dans Edge).
        IsTextToolActive = false;
        if (HasTextSelection && CanEdit)
        {
            _ = HighlightSelectedTextCommand.ExecuteAsync(null);
        }
    }

    partial void OnSelectedHighlightChanged(PdfHighlight? value)
    {
        if (value is not null)
        {
            SelectTextBox(null);
        }

        DeleteSelectedHighlightCommand.NotifyCanExecuteChanged();
        HighlightsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshHighlightCommands()
    {
        HighlightSelectedTextCommand.NotifyCanExecuteChanged();
        DeleteSelectedHighlightCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Nouveau contenu : surlignages relus à la demande, sélection oubliée.</summary>
    private void ResetHighlights()
    {
        _highlights.Clear();
        SelectedHighlight = null;
        if (!AcceptsTextBoxes)
        {
            IsHighlightToolActive = false;
        }

        HighlightsChanged?.Invoke(this, EventArgs.Empty);
    }
}
