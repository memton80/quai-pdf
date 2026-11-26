using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.ViewModels;

/// <summary>
/// Sélection et copie du texte des pages, comme dans un navigateur : appuyer sur du texte puis glisser, double-clic
/// pour un mot, Ctrl+A pour tout le document, Ctrl+C pour copier. Le texte est lu par PDFium.
/// </summary>
public sealed partial class DocumentViewModel
{
    /// <summary>Distance (pixels à 100 %) jusqu'à laquelle un double-clic désigne un mot.</summary>
    public const double TextHitTolerance = 3;

    private PdfTextDocument? _text;
    private bool _textUnavailable;

    /// <summary>La sélection de texte a changé : les surlignages sont à redessiner.</summary>
    public event EventHandler? TextSelectionChanged;

    public PdfTextSelection? TextSelection { get; private set; }

    public bool HasTextSelection => TextSelection is { IsEmpty: false };

    /// <summary>Texte sélectionné, pages séparées par une fin de ligne.</summary>
    public string SelectedText
    {
        get
        {
            if (TextSelection is not { } selection || Text is not { } text)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            for (var page = selection.Start.Page; page <= selection.End.Page && page < text.PageCount; page++)
            {
                var textPage = text.GetPage(page);
                if (selection.RangeOn(page, textPage.CharCount) is { } range)
                {
                    parts.Add(textPage.Text(range.From, range.To).TrimEnd('\r', '\n'));
                }
            }

            return string.Join(Environment.NewLine, parts);
        }
    }

    /// <summary>Texte du contenu affiché, ouvert à la première demande ; <c>null</c> si PDFium ne peut pas le lire.</summary>
    internal PdfTextDocument? Text
    {
        get
        {
            if (_text is null && !_textUnavailable && Content is { } content)
            {
                try
                {
                    _text = PdfTextDocument.Open(content, Password);
                }
                catch (Exception ex) when (ex is PdfFileException or PdfPasswordException or DllNotFoundException or EntryPointNotFoundException)
                {
                    // Le document reste lisible et modifiable : seule la sélection de texte manque.
                    _textUnavailable = true;
                    _logger.LogWarning(ex, "Texte du document illisible : sélection indisponible");
                }
            }

            return _text;
        }
    }

    /// <summary>Texte d'une page, ou <c>null</c> (pas de document, page hors du document, texte illisible).</summary>
    public PdfTextPage? TextPage(int page) =>
        Text is { } text && page >= 0 && page < text.PageCount ? text.GetPage(page) : null;

    /// <summary>
    /// Appui sur une page, comme dans un navigateur : la sélection précédente s'efface et une nouvelle part du texte le
    /// plus proche (elle reste vide tant qu'on ne glisse pas). Avec <paramref name="extend"/> (touche Maj), la
    /// sélection en cours va jusqu'à ce point. Retourne vrai si un glisser peut suivre (la page a du texte).
    /// </summary>
    public bool BeginTextSelection(int page, PdfPoint point, bool extend)
    {
        if (TextPage(page) is not { CharCount: > 0 } textPage)
        {
            ClearTextSelection();
            return false;
        }

        var caret = new PdfTextPosition(page, textPage.CaretAt(point));
        SetTextSelection(extend && TextSelection is { } current
            ? current with { Focus = caret }
            : new PdfTextSelection(caret, caret));
        return true;
    }

    /// <summary>Glisser : la sélection va jusqu'au texte le plus proche du point, sur cette page.</summary>
    public void ExtendTextSelection(int page, PdfPoint point)
    {
        if (TextSelection is not { } current || TextPage(page) is not { } textPage)
        {
            return;
        }

        // Page sans texte (scan) traversée pendant le glisser : la sélection s'arrête à son début.
        var caret = textPage.CharCount > 0 ? textPage.CaretAt(point) : 0;
        var focus = new PdfTextPosition(page, caret);
        if (focus != current.Focus)
        {
            SetTextSelection(current with { Focus = focus });
        }
    }

    /// <summary>Double-clic : sélectionne le mot sous le point.</summary>
    public void SelectWordAt(int page, PdfPoint point)
    {
        if (TextPage(page) is not { } textPage)
        {
            return;
        }

        var index = textPage.HitTest(point, TextHitTolerance);
        if (index < 0)
        {
            return;
        }

        var (start, end) = textPage.WordAt(index);
        SetTextSelection(new PdfTextSelection(new PdfTextPosition(page, start), new PdfTextPosition(page, end + 1)));
    }

    public void ClearTextSelection()
    {
        if (TextSelection is not null)
        {
            SetTextSelection(null);
        }
    }

    /// <summary>Rectangles à surligner sur une page (pixels à 100 %).</summary>
    public IReadOnlyList<PdfRect> TextSelectionRects(int page)
    {
        if (TextSelection is not { } selection || TextPage(page) is not { } textPage
            || selection.RangeOn(page, textPage.CharCount) is not { } range)
        {
            return [];
        }

        return textPage.Rects(range.From, range.To);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SelectAllText()
    {
        if (Text is not { PageCount: > 0 } text)
        {
            return;
        }

        var first = Enumerable.Range(0, text.PageCount).FirstOrDefault(p => text.GetPage(p).CharCount > 0, -1);
        if (first < 0)
        {
            ClearTextSelection();
            return;
        }

        var last = Enumerable.Range(0, text.PageCount).Last(p => text.GetPage(p).CharCount > 0);
        SetTextSelection(new PdfTextSelection(
            new PdfTextPosition(first, 0),
            new PdfTextPosition(last, text.GetPage(last).CharCount)));
    }

    [RelayCommand(CanExecute = nameof(HasTextSelection))]
    private async Task CopyTextAsync()
    {
        if (Text is { CanCopy: false })
        {
            await _dialogs.ShowErrorAsync(_strings.Get("Error_CopyTitle"), _strings.Get("Error_CopyForbidden"));
            return;
        }

        var selected = SelectedText;
        if (selected.Length == 0)
        {
            return;
        }

        try
        {
            _clipboard.SetText(selected);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Presse-papiers occupé par une autre application : on le dit plutôt que de perdre la copie en silence.
            _logger.LogWarning(ex, "Copie dans le presse-papiers impossible");
            await _dialogs.ShowErrorAsync(_strings.Get("Error_CopyTitle"), _strings.Get("Error_Clipboard"));
        }
    }

    private void SetTextSelection(PdfTextSelection? selection)
    {
        TextSelection = selection;
        OnPropertyChanged(nameof(TextSelection));
        OnPropertyChanged(nameof(HasTextSelection));
        CopyTextCommand.NotifyCanExecuteChanged();
        TextSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Nouveau contenu ou document fermé : texte relu à la prochaine demande, sélection effacée, plan et recherche
    /// oubliés.
    /// </summary>
    private void ResetText()
    {
        ResetPanels();
        _text?.Dispose();
        _text = null;
        _textUnavailable = false;
        if (TextSelection is not null)
        {
            SetTextSelection(null);
        }
    }
}
