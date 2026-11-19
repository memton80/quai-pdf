using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuaiPdf.Core.Localization;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.Storage;

namespace QuaiPdf.Core.ViewModels;

/// <summary>Le contenu affiché vient de changer : pages à resélectionner et page à montrer.</summary>
public sealed class DocumentChangedEventArgs(IReadOnlyList<int> selection, int? scrollTo) : EventArgs
{
    /// <summary>Indices des pages à sélectionner dans les vignettes.</summary>
    public IReadOnlyList<int> Selection { get; } = selection;

    /// <summary>Page à faire défiler jusqu'à l'écran, ou <c>null</c> pour rester où l'on est.</summary>
    public int? ScrollTo { get; } = scrollTo;
}

/// <summary>
/// Le document ouvert : lecture (pages, zoom, navigation) et modifications de base (rotation, suppression, ordre,
/// insertion, extraction, propriétés), avec annuler / rétablir. Le fichier n'est écrit qu'à l'enregistrement.
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject, IDisposable
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 5;

    // Zoom maximal choisi automatiquement à l'ouverture (« ajuster à la largeur » sur un grand écran).
    private const double MaxOpeningZoom = 1.5;

    // Marges autour des pages dans la zone de lecture (voir PagePadding dans l'application).
    private const double ViewportMargin = 48;

    private static readonly double[] ZoomSteps = [0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

    private readonly IPdfRenderer _renderer;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogs _files;
    private readonly IStrings _strings;
    private readonly ILogger _logger;
    private readonly PdfHistory _history = new();

    // Contenu tel qu'il est sur le disque : comparé au contenu affiché pour savoir s'il reste à enregistrer.
    private byte[]? _savedContent;

    public DocumentViewModel(
        IPdfRenderer renderer,
        IDialogService dialogs,
        IFileDialogs files,
        IStrings strings,
        ILogger<DocumentViewModel>? logger = null)
    {
        _renderer = renderer;
        _dialogs = dialogs;
        _files = files;
        _strings = strings;
        _logger = logger ?? NullLogger<DocumentViewModel>.Instance;
        Zoom = 1;
        CurrentPageNumber = 1;
    }

    /// <summary>Le contenu a changé (ouverture, modification, annulation) : les images des pages sont à redessiner.</summary>
    public event EventHandler<DocumentChangedEventArgs>? ContentChanged;

    /// <summary>Demande de faire défiler la lecture jusqu'à une page (indice).</summary>
    public event EventHandler<int>? ScrollRequested;

    public ObservableCollection<PageItemViewModel> Pages { get; } = [];

    /// <summary>Contenu affiché (le fichier d'origine n'est jamais modifié avant l'enregistrement).</summary>
    public byte[]? Content { get; private set; }

    /// <summary>Contenu prêt à dessiner, fourni par le moteur d'affichage.</summary>
    public IRenderedPdf? Rendered { get; private set; }

    /// <summary>Mot de passe saisi à l'ouverture : en mémoire seulement, jamais écrit ni journalisé.</summary>
    internal string? Password { get; private set; }

    /// <summary>Pages sélectionnées dans les vignettes (indices croissants).</summary>
    public IReadOnlyList<int> SelectedIndices { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(WindowTitle))]
    public partial string? FilePath { get; private set; }

    public string FileName => FilePath is null ? string.Empty : Path.GetFileName(FilePath);

    public string WindowTitle => HasDocument
        ? $"{(IsModified ? "*" : string.Empty)}{FileName} - {AppInfo.DisplayName}"
        : AppInfo.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(CanEdit), nameof(IsEmpty), nameof(IsReadOnly), nameof(ReadOnlyMessage))]
    public partial bool HasDocument { get; private set; }

    public bool IsEmpty => !HasDocument;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial bool IsModified { get; private set; }

    /// <summary>Opération en cours (lecture, modification, enregistrement) : les commandes attendent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(ReadOnlyMessage), nameof(IsReadOnly))]
    public partial PdfEditability Editability { get; private set; }

    public bool CanEdit => HasDocument && !IsBusy && Editability == PdfEditability.Editable;

    public bool IsReadOnly => HasDocument && Editability != PdfEditability.Editable;

    /// <summary>Pourquoi le document s'affiche en lecture seule (vide s'il est modifiable).</summary>
    public string ReadOnlyMessage => !HasDocument ? string.Empty : Editability switch
    {
        PdfEditability.Protected => _strings.Get("Document_ReadOnlyProtected"),
        PdfEditability.Unsupported => _strings.Get("Document_ReadOnlyUnsupported"),
        _ => string.Empty,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    public partial double Zoom { get; set; }

    public string ZoomText => $"{Math.Round(Zoom * 100)} %";

    /// <summary>Largeur de la zone de lecture (renseignée par la vue) : sert à « Ajuster à la largeur ».</summary>
    public double ViewportWidth { get; set; }

    /// <summary>Page visible (à partir de 1), mise à jour par la vue au défilement.</summary>
    [ObservableProperty]
    public partial int CurrentPageNumber { get; set; }

    public int PageCount => Pages.Count;

    /// <summary>Pages visées par une commande : la sélection, sinon la page visible.</summary>
    public IReadOnlyList<int> TargetIndices => SelectedIndices.Count > 0
        ? SelectedIndices
        : HasDocument ? [Math.Clamp(CurrentPageNumber, 1, Math.Max(1, PageCount)) - 1] : [];

    public bool CanUndo => CanEdit && _history.CanUndo;

    public bool CanRedo => CanEdit && _history.CanRedo;

    public void SetSelection(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        SelectedIndices = indices.Where(i => i >= 0 && i < PageCount).Distinct().Order().ToList();
        RefreshCommands();
    }

    public void GoToPage(int number)
    {
        if (PageCount == 0)
        {
            return;
        }

        CurrentPageNumber = Math.Clamp(number, 1, PageCount);
        ScrollRequested?.Invoke(this, CurrentPageNumber - 1);
    }

    /// <summary>Ouvre un fichier (commande « Ouvrir », ligne de commande, glisser-déposer).</summary>
    public async Task<bool> OpenFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsBusy || !await ConfirmCloseAsync())
        {
            return false;
        }

        IsBusy = true;
        try
        {
            return await LoadFileAsync(Path.GetFullPath(path));
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    /// <summary>
    /// Avant de fermer le document (autre fichier, fermeture de la fenêtre) : propose d'enregistrer les modifications.
    /// Retourne faux si l'utilisateur annule.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!HasDocument || !IsModified)
        {
            return true;
        }

        return await _dialogs.AskSaveChangesAsync(FileName) switch
        {
            SaveChoice.Save => await SaveCoreAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    public void Dispose()
    {
        Rendered?.Dispose();
        Rendered = null;
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task OpenAsync()
    {
        var path = await _files.PickPdfToOpenAsync();
        if (path is not null)
        {
            await OpenFileAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveCoreAsync();

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private Task SaveAsAsync() => SaveAsCoreAsync();

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task CloseDocumentAsync()
    {
        if (!await ConfirmCloseAsync())
        {
            return;
        }

        Reset();
        _logger.LogInformation("Document fermé");
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => StepAsync(undo: true);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task RedoAsync() => StepAsync(undo: false);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RotateLeftAsync() => RotateAsync(-90);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RotateRightAsync() => RotateAsync(90);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var targets = TargetIndices;
        var first = targets.Min();
        await ApplyAsync(
            content => PdfOperations.Delete(content, targets),
            count => [Math.Min(first, count - 1)],
            scrollToSelection: true);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync() => MoveAsync(1);

    /// <summary>Nouvel ordre des pages après un glisser-déposer dans les vignettes (indices d'origine).</summary>
    public async Task ReorderAsync(IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.SequenceEqual(Enumerable.Range(0, order.Count)))
        {
            return;
        }

        var selected = new HashSet<int>(SelectedIndices);
        var selection = Enumerable.Range(0, order.Count).Where(position => selected.Contains(order[position])).ToList();
        if (!await ApplyAsync(content => PdfOperations.Reorder(content, order), _ => selection, scrollToSelection: false))
        {
            // Les vignettes ont déjà été déplacées par le glisser-déposer : elles reprennent l'ordre du contenu.
            RestorePageOrder();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task InsertAsync()
    {
        var paths = await _files.PickPdfsToInsertAsync();
        if (paths.Count == 0)
        {
            return;
        }

        var sources = new List<byte[]>(paths.Count);
        foreach (var path in paths)
        {
            var source = await ReadSourceAsync(path);
            if (source is null)
            {
                return;
            }

            sources.Add(source);
        }

        // Après la dernière page visée (la page visible, ou la sélection).
        var position = TargetIndices.Max() + 1;
        var before = PageCount;
        await ApplyAsync(
            content => PdfOperations.Insert(content, position, sources),
            count => Enumerable.Range(position, count - before).ToList(),
            scrollToSelection: true);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ExtractAsync()
    {
        var content = Content;
        if (content is null)
        {
            return;
        }

        var pages = TargetIndices;
        var suggested = _strings.Format("Document_ExtractFileName", Path.GetFileNameWithoutExtension(FileName));
        var path = await _files.PickSaveLocationAsync(suggested, Path.GetDirectoryName(FilePath));
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => AtomicFile.Write(path, PdfOperations.Extract(content, pages)));
            _logger.LogInformation("{Count} page(s) extraite(s) dans un nouveau fichier", pages.Count);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ReportAsync("Error_ExtractTitle", ex);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditPropertiesAsync()
    {
        var content = Content;
        if (content is null)
        {
            return;
        }

        PdfMetadata metadata;
        try
        {
            metadata = await Task.Run(() => PdfOperations.ReadMetadata(content));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ReportAsync("Error_EditTitle", ex);
            return;
        }

        var editor = new MetadataEditorViewModel(metadata);
        if (!await _dialogs.EditMetadataAsync(editor) || editor.Result == metadata)
        {
            return;
        }

        var selection = SelectedIndices;
        await ApplyAsync(c => PdfOperations.WriteMetadata(c, editor.Result), _ => selection, scrollToSelection: false);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ZoomIn() => Zoom = ZoomSteps.FirstOrDefault(step => step > Zoom + 0.001, MaxZoom);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ZoomOut() => Zoom = ZoomSteps.LastOrDefault(step => step < Zoom - 0.001, MinZoom);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ActualSize() => Zoom = 1;

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FitWidth()
    {
        if (FitWidthZoom() is { } zoom)
        {
            Zoom = zoom;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage() => GoToPage(CurrentPageNumber - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => GoToPage(CurrentPageNumber + 1);

    private bool CanRunCommand() => !IsBusy;

    private bool CanSave() => CanEdit && (IsModified || FilePath is null);

    private bool CanSaveAs() => HasDocument && !IsBusy;

    private bool CanDelete() => CanEdit && TargetIndices.Count < PageCount;

    private bool CanMoveUp() => CanEdit && !IsIdentity(PdfOperations.MoveOrder(PageCount, TargetIndices, -1));

    private bool CanMoveDown() => CanEdit && !IsIdentity(PdfOperations.MoveOrder(PageCount, TargetIndices, 1));

    private bool CanGoPrevious() => HasDocument && CurrentPageNumber > 1;

    private bool CanGoNext() => HasDocument && CurrentPageNumber < PageCount;

    partial void OnZoomChanged(double value)
    {
        var clamped = double.IsNaN(value) ? 1 : Math.Clamp(value, MinZoom, MaxZoom);
        if (clamped != value)
        {
            Zoom = clamped;
            return;
        }

        foreach (var page in Pages)
        {
            page.Zoom = value;
        }
    }

    partial void OnCurrentPageNumberChanged(int value)
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        RefreshTargetCommands();
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    private async Task<bool> LoadFileAsync(string path)
    {
        var name = Path.GetFileName(path);
        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Lecture de {File} impossible", name);
            await _dialogs.ShowErrorAsync(_strings.Get("Error_OpenTitle"), _strings.Format("Error_FileAccess", name));
            return false;
        }

        string? password = null;
        IRenderedPdf rendered;
        while (true)
        {
            try
            {
                rendered = await _renderer.LoadAsync(content, password);
                break;
            }
            catch (PdfPasswordException)
            {
                password = await _dialogs.PromptPasswordAsync(name, isRetry: password is not null);
                if (password is null)
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Affichage de {File} impossible", name);
                await _dialogs.ShowErrorAsync(_strings.Get("Error_OpenTitle"), _strings.Format("Error_Unreadable", name));
                return false;
            }
        }

        // Un document qui demande un mot de passe reste en lecture seule (sa protection n'est jamais retirée en silence).
        var editability = password is not null
            ? PdfEditability.Protected
            : await Task.Run(() => PdfOperations.Inspect(content));

        Reset();
        Password = password;
        FilePath = path;
        Editability = editability;
        HasDocument = true;
        _savedContent = content;
        Show(content, rendered, [], scrollTo: 0);
        if (FitWidthZoom() is { } zoom)
        {
            Zoom = Math.Min(zoom, MaxOpeningZoom);
        }

        _logger.LogInformation("Ouverture de {File} : {Pages} page(s), {Editability}", name, PageCount, editability);
        return true;
    }

    private async Task<byte[]?> ReadSourceAsync(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            var source = await File.ReadAllBytesAsync(path);
            await Task.Run(() => PdfOperations.PageCount(source));
            return source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(_strings.Get("Error_InsertTitle"), _strings.Format("Error_FileAccess", name));
        }
        catch (PdfFileException ex)
        {
            await _dialogs.ShowErrorAsync(
                _strings.Get("Error_InsertTitle"),
                _strings.Format(ex.Problem == PdfProblem.Protected ? "Error_SourceProtected" : "Error_Unreadable", name));
        }

        return null;
    }

    private async Task RotateAsync(int degrees)
    {
        var targets = TargetIndices;
        var selection = SelectedIndices;
        await ApplyAsync(content => PdfOperations.Rotate(content, targets, degrees), _ => selection, scrollToSelection: false);
    }

    private async Task MoveAsync(int offset)
    {
        var order = PdfOperations.MoveOrder(PageCount, TargetIndices, offset);
        var moved = new HashSet<int>(TargetIndices);
        var selection = Enumerable.Range(0, order.Count).Where(position => moved.Contains(order[position])).ToList();
        await ApplyAsync(content => PdfOperations.Reorder(content, order), _ => selection, scrollToSelection: true);
    }

    /// <summary>Applique une modification au contenu affiché, en arrière-plan, et l'inscrit dans l'historique.</summary>
    /// <param name="selectionAfter">Pages à sélectionner ensuite, selon le nouveau nombre de pages.</param>
    /// <returns>Vrai si la modification a été appliquée.</returns>
    private async Task<bool> ApplyAsync(Func<byte[], byte[]> change, Func<int, IReadOnlyList<int>> selectionAfter, bool scrollToSelection)
    {
        var before = Content;
        if (before is null || !CanEdit)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var after = await Task.Run(() => change(before));
            var rendered = await _renderer.LoadAsync(after, Password);
            var selection = selectionAfter(rendered.Pages.Count);
            _history.Record(before);
            Show(after, rendered, selection, scrollToSelection && selection.Count > 0 ? selection[0] : null);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ReportAsync("Error_EditTitle", ex);
            return false;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    /// <summary>Remet les pages dans l'ordre de leur <see cref="PageItemViewModel.Index"/>.</summary>
    private void RestorePageOrder()
    {
        for (var position = 0; position < Pages.Count; position++)
        {
            var current = Pages.IndexOf(Pages.First(p => p.Index == position));
            if (current != position)
            {
                Pages.Move(current, position);
            }
        }
    }

    private async Task StepAsync(bool undo)
    {
        var current = Content;
        var target = undo ? _history.UndoTarget : _history.RedoTarget;
        if (current is null || target is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var rendered = await _renderer.LoadAsync(target, Password);
            if (undo)
            {
                _history.Undone(current);
            }
            else
            {
                _history.Redone(current);
            }

            Show(target, rendered, [], scrollTo: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ReportAsync("Error_EditTitle", ex);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    /// <summary>Enregistre le contenu affiché à cet emplacement (test de fumée, sans sélecteur de fichier).</summary>
    public Task<bool> SaveToAsync(string path) => WriteAsync(path);

    private async Task<bool> SaveCoreAsync()
    {
        if (FilePath is null)
        {
            return await SaveAsCoreAsync();
        }

        return await WriteAsync(FilePath);
    }

    private async Task<bool> SaveAsCoreAsync()
    {
        if (!HasDocument)
        {
            return false;
        }

        var path = await _files.PickSaveLocationAsync(FileName, Path.GetDirectoryName(FilePath));
        return path is not null && await WriteAsync(path);
    }

    private async Task<bool> WriteAsync(string path)
    {
        var content = Content;
        if (content is null)
        {
            return false;
        }

        var wasBusy = IsBusy;
        IsBusy = true;
        try
        {
            await Task.Run(() => AtomicFile.Write(path, content));
            _savedContent = content;
            FilePath = Path.GetFullPath(path);
            IsModified = false;
            _logger.LogInformation("Document enregistré ({File})", FileName);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Enregistrement de {File} impossible", Path.GetFileName(path));
            await _dialogs.ShowErrorAsync(_strings.Get("Error_SaveTitle"), _strings.Format("Error_SaveAccess", Path.GetFileName(path)));
            return false;
        }
        finally
        {
            IsBusy = wasBusy;
            RefreshCommands();
        }
    }

    /// <summary>Affiche un nouveau contenu : pages mises à jour sur place (les vignettes ne clignotent pas).</summary>
    private void Show(byte[] content, IRenderedPdf rendered, IReadOnlyList<int> selection, int? scrollTo)
    {
        var previous = Rendered;
        Content = content;
        Rendered = rendered;

        var sizes = rendered.Pages;
        while (Pages.Count > sizes.Count)
        {
            Pages.RemoveAt(Pages.Count - 1);
        }

        for (var i = 0; i < sizes.Count; i++)
        {
            if (i < Pages.Count)
            {
                Pages[i].Index = i;
                Pages[i].Size = sizes[i];
                Pages[i].Zoom = Zoom;
            }
            else
            {
                Pages.Add(new PageItemViewModel(i, sizes[i], Zoom));
            }
        }

        OnPropertyChanged(nameof(PageCount));
        SelectedIndices = selection.Where(i => i >= 0 && i < sizes.Count).ToList();
        CurrentPageNumber = Math.Clamp(scrollTo is { } page ? page + 1 : CurrentPageNumber, 1, Math.Max(1, sizes.Count));
        IsModified = !ReferenceEquals(content, _savedContent);
        ContentChanged?.Invoke(this, new DocumentChangedEventArgs(SelectedIndices, scrollTo));
        RefreshCommands();
        previous?.Dispose();
    }

    private void Reset()
    {
        _history.Clear();
        Rendered?.Dispose();
        Rendered = null;
        Content = null;
        _savedContent = null;
        Password = null;
        Pages.Clear();
        SelectedIndices = [];
        FilePath = null;
        HasDocument = false;
        IsModified = false;
        Editability = PdfEditability.Editable;
        CurrentPageNumber = 1;
        OnPropertyChanged(nameof(PageCount));
        ContentChanged?.Invoke(this, new DocumentChangedEventArgs([], null));
        RefreshCommands();
    }

    private double? FitWidthZoom()
    {
        if (Pages.Count == 0 || ViewportWidth <= ViewportMargin)
        {
            return null;
        }

        var widest = Pages.Max(p => p.Size.Width);
        return widest > 0 ? Math.Clamp((ViewportWidth - ViewportMargin) / widest, MinZoom, MaxZoom) : null;
    }

    private async Task ReportAsync(string titleKey, Exception ex)
    {
        _logger.LogError(ex, "Opération sur le document en échec");
        var message = ex switch
        {
            PdfFileException { Problem: PdfProblem.Protected } => _strings.Get("Error_Protected"),
            PdfFileException => _strings.Format("Error_Unreadable", FileName),
            IOException or UnauthorizedAccessException => _strings.Get("Error_WriteAccess"),
            _ => _strings.Format("Error_Unexpected", ex.Message),
        };
        await _dialogs.ShowErrorAsync(_strings.Get(titleKey), message);
    }

    private static bool IsIdentity(IReadOnlyList<int> order) => order.SequenceEqual(Enumerable.Range(0, order.Count));

    private void RefreshTargetCommands()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OpenCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
        InsertCommand.NotifyCanExecuteChanged();
        ExtractCommand.NotifyCanExecuteChanged();
        EditPropertiesCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ActualSizeCommand.NotifyCanExecuteChanged();
        FitWidthCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        RefreshTargetCommands();
    }
}
