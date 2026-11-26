using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.ViewModels;

/// <summary>Une entrée du plan (signet) affichée dans le volet « Plan ».</summary>
public sealed class OutlineItemViewModel
{
    public OutlineItemViewModel(string title, int pageIndex, string pageLabel, IReadOnlyList<OutlineItemViewModel> children)
    {
        Title = title;
        PageIndex = pageIndex;
        PageLabel = pageLabel;
        Children = children;
    }

    public string Title { get; }

    /// <summary>Page désignée (à partir de 0), ou -1.</summary>
    public int PageIndex { get; }

    /// <summary>« p. 3 », vide si le signet ne désigne aucune page.</summary>
    public string PageLabel { get; }

    public IReadOnlyList<OutlineItemViewModel> Children { get; }

    public bool HasChildren => Children.Count > 0;
}

/// <summary>Une occurrence trouvée par la recherche : sa page, sa place dans le texte et un extrait.</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(int page, PdfTextMatch match, string before, string text, string after, string pageLabel)
    {
        Page = page;
        Match = match;
        Before = before;
        Text = text;
        After = after;
        PageLabel = pageLabel;
    }

    public int Page { get; }

    public PdfTextMatch Match { get; }

    public string Before { get; }

    /// <summary>Le texte trouvé, tel qu'il est écrit dans le document.</summary>
    public string Text { get; }

    public string After { get; }

    public string PageLabel { get; }

    /// <summary>Nom d'accessibilité : page et extrait.</summary>
    public string AccessibleName => $"{PageLabel} : {Before}{Text}{After}";
}

/// <summary>Une ligne du volet « Propriétés » (libellé, valeur).</summary>
public sealed record DocumentPropertyItem(string Label, string Value);

/// <summary>Demande de montrer une zone d'une page (occurrence trouvée) : la vue fait défiler jusqu'à elle.</summary>
public sealed class RevealRequestedEventArgs(int page, PdfRect area) : EventArgs
{
    public int Page { get; } = page;

    /// <summary>Zone à montrer, en pixels à 100 % sur la page.</summary>
    public PdfRect Area { get; } = area;
}

/// <summary>Volets latéraux du document : plan (signets), recherche dans le texte, propriétés.</summary>
public sealed partial class DocumentViewModel
{
    /// <summary>Au-delà, la recherche s'arrête (les premières occurrences suffisent à naviguer).</summary>
    public const int MaxSearchResults = 1000;

    private const string NoValue = "—";

    private IReadOnlyList<OutlineItemViewModel>? _outline;

    /// <summary>Une occurrence trouvée est à montrer à l'écran.</summary>
    public event EventHandler<RevealRequestedEventArgs>? RevealRequested;

    // ----- Plan -----

    /// <summary>
    /// Plan du document affiché, lu à la première demande (le volet « Plan » le demande quand il s'ouvre, puis après
    /// chaque modification). Liste vide si le document n'en a pas ou si son texte est illisible.
    /// </summary>
    public IReadOnlyList<OutlineItemViewModel> Outline => _outline ??= LoadOutline();

    /// <summary>Montre la page d'une entrée du plan.</summary>
    public void GoToOutlineItem(OutlineItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.PageIndex >= 0)
        {
            GoToPage(item.PageIndex + 1);
        }
    }

    private IReadOnlyList<OutlineItemViewModel> LoadOutline()
    {
        if (Text is not { } text)
        {
            return [];
        }

        try
        {
            return Convert(text.Outline());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Plan du document illisible");
            return [];
        }

        List<OutlineItemViewModel> Convert(IReadOnlyList<PdfOutlineItem> items) =>
            [.. items.Select(i => new OutlineItemViewModel(
                i.Title.Length > 0 ? i.Title : NoValue,
                i.PageIndex,
                i.PageIndex >= 0 ? _strings.Format("Outline_Page", i.PageIndex + 1) : string.Empty,
                Convert(i.Children)))];
    }

    // ----- Recherche -----

    /// <summary>Texte cherché (champ de recherche de la barre de commandes).</summary>
    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    /// <summary>Résumé sous le champ : nombre d'occurrences, aucune, ou texte illisible.</summary>
    [ObservableProperty]
    public partial string SearchStatus { get; private set; } = string.Empty;

    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    /// <summary>Occurrence montrée (surlignée dans la page et sélectionnée dans la liste).</summary>
    [ObservableProperty]
    public partial SearchResultViewModel? CurrentSearchResult { get; private set; }

    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>Montre une occurrence : sélectionnée comme du texte (surlignée, prête à copier), page à l'écran.</summary>
    public void ShowSearchResult(SearchResultViewModel result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (TextPage(result.Page) is not { } page)
        {
            return;
        }

        CurrentSearchResult = result;
        var end = result.Match.Start + result.Match.Length;
        SetTextSelection(new PdfTextSelection(new PdfTextPosition(result.Page, result.Match.Start), new PdfTextPosition(result.Page, end)));
        var area = page.Rects(result.Match.Start, end - 1) is [var first, ..] ? first : new PdfRect(0, 0, 0, 0);
        CurrentPageNumber = result.Page + 1;
        RevealRequested?.Invoke(this, new RevealRequestedEventArgs(result.Page, area));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SearchAsync()
    {
        ClearSearchResults();
        var query = SearchQuery.Trim();
        if (query.Length == 0 || IsBusy)
        {
            return;
        }

        if (Text is not { } text)
        {
            SearchStatus = _strings.Get("Search_Unavailable");
            return;
        }

        IsBusy = true;
        try
        {
            var culture = CultureInfo.CurrentCulture;
            var found = await Task.Run(() => Find(text, query, culture));
            foreach (var result in found)
            {
                SearchResults.Add(result);
            }

            SearchStatus = found.Count switch
            {
                0 => _strings.Format("Search_None", query),
                1 => _strings.Get("Search_One"),
                >= MaxSearchResults => _strings.Format("Search_Many", MaxSearchResults),
                _ => _strings.Format("Search_Count", found.Count),
            };
            _logger.LogInformation("Recherche : {Count} occurrence(s)", found.Count);
        }
        catch (ObjectDisposedException)
        {
            // Document refermé pendant la recherche : il n'y a plus rien à montrer.
            ClearSearchResults();
        }
        finally
        {
            IsBusy = false;
            RefreshSearchCommands();
        }

        if (SearchResults.Count > 0)
        {
            ShowSearchResult(SearchResults[0]);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSearchResults))]
    private void NextSearchResult() => StepSearchResult(1);

    [RelayCommand(CanExecute = nameof(HasSearchResults))]
    private void PreviousSearchResult() => StepSearchResult(-1);

    private void StepSearchResult(int offset)
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        var index = CurrentSearchResult is { } current ? SearchResults.IndexOf(current) : -1;
        index = index < 0 ? (offset > 0 ? 0 : SearchResults.Count - 1) : (index + offset + SearchResults.Count) % SearchResults.Count;
        ShowSearchResult(SearchResults[index]);
    }

    private List<SearchResultViewModel> Find(PdfTextDocument text, string query, CultureInfo culture)
    {
        var results = new List<SearchResultViewModel>();
        for (var page = 0; page < text.PageCount && results.Count < MaxSearchResults; page++)
        {
            var content = text.GetPage(page).AllText();
            var label = _strings.Format("Page_Name", page + 1);
            foreach (var match in PdfTextSearch.Find(content, query, MaxSearchResults - results.Count, culture))
            {
                var (before, found, after) = PdfTextSearch.Excerpt(content, match);
                results.Add(new SearchResultViewModel(page, match, before, found, after, label));
            }
        }

        return results;
    }

    /// <summary>Le contenu a changé : les positions trouvées ne valent plus rien (la recherche est à relancer).</summary>
    private void ClearSearchResults()
    {
        CurrentSearchResult = null;
        SearchResults.Clear();
        SearchStatus = string.Empty;
        RefreshSearchCommands();
    }

    private void RefreshSearchCommands()
    {
        OnPropertyChanged(nameof(HasSearchResults));
        NextSearchResultCommand.NotifyCanExecuteChanged();
        PreviousSearchResultCommand.NotifyCanExecuteChanged();
    }

    // ----- Propriétés -----

    /// <summary>Lignes du volet « Propriétés », remplies par <see cref="RefreshPropertiesAsync"/>.</summary>
    public ObservableCollection<DocumentPropertyItem> Properties { get; } = [];

    /// <summary>Relit les propriétés du document affiché (à l'ouverture du volet, puis après chaque modification).</summary>
    public async Task RefreshPropertiesAsync()
    {
        var content = Content;
        Properties.Clear();
        if (content is null)
        {
            return;
        }

        PdfMetadata? metadata = null;
        try
        {
            metadata = await Task.Run(() => PdfOperations.ReadMetadata(content));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Document protégé ou que PDFsharp ne sait pas lire : seules les informations sur le fichier s'affichent.
            _logger.LogInformation("Propriétés du document non lues ({Error})", ex.GetType().Name);
        }

        // Le document a pu changer (ou se fermer) pendant la lecture : la plus récente demande l'emporte.
        if (!ReferenceEquals(content, Content))
        {
            return;
        }

        Properties.Clear();
        if (metadata is not null)
        {
            Add("Metadata_DocumentTitle", metadata.Title);
            Add("Metadata_Author", metadata.Author);
            Add("Metadata_Subject", metadata.Subject);
            Add("Metadata_Keywords", metadata.Keywords);
        }

        Add("Properties_Pages", PageCount.ToString(CultureInfo.CurrentCulture));
        Add("Properties_Size", FormatSize(content.Length));
        Add("Properties_File", FileName);
        Add("Properties_Folder", Path.GetDirectoryName(FilePath) ?? string.Empty);
        Add("Properties_Editing", _strings.Get(Editability switch
        {
            PdfEditability.Editable => "Properties_Editable",
            PdfEditability.Protected => "Properties_Protected",
            _ => "Properties_Unsupported",
        }));
        if (Text is { } text)
        {
            Add("Properties_Copy", _strings.Get(text.CanCopy ? "Properties_CopyAllowed" : "Properties_CopyForbidden"));
        }

        void Add(string labelKey, string value) =>
            Properties.Add(new DocumentPropertyItem(_strings.Get(labelKey), string.IsNullOrWhiteSpace(value) ? NoValue : value.Trim()));
    }

    /// <summary>Taille d'un fichier en octets, Ko ou Mo (« 1,4 Mo »).</summary>
    internal string FormatSize(long bytes) => bytes switch
    {
        < 1024 => _strings.Format("Size_Bytes", bytes),
        < 1024 * 1024 => _strings.Format("Size_Kilobytes", (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture)),
        _ => _strings.Format("Size_Megabytes", (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture)),
    };

    /// <summary>Nouveau contenu ou document fermé : plan à relire, recherche à relancer.</summary>
    private void ResetPanels()
    {
        _outline = null;
        ClearSearchResults();
    }
}
