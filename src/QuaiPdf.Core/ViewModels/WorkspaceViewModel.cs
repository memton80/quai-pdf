using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuaiPdf.Core.Services;

namespace QuaiPdf.Core.ViewModels;

/// <summary>
/// Les onglets de la fenêtre : un document par onglet, chacun avec son historique et sa propre vue. Il reste toujours
/// au moins un onglet : fermer le dernier le ramène à l'accueil (aucun document).
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly Func<DocumentViewModel> _createDocument;
    private readonly IFileDialogs _files;
    private readonly ILogger _logger;

    public WorkspaceViewModel(Func<DocumentViewModel> createDocument, IFileDialogs files, ILogger<WorkspaceViewModel>? logger = null)
    {
        _createDocument = createDocument;
        _files = files;
        _logger = logger ?? NullLogger<WorkspaceViewModel>.Instance;
        var first = _createDocument();
        Documents.Add(first);
        Subscribe(first);
        Selected = first;
    }

    /// <summary>Documents ouverts, dans l'ordre des onglets.</summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>Onglet affiché (jamais <c>null</c> : il reste toujours un onglet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedCommand))]
    public partial DocumentViewModel Selected { get; set; }

    /// <summary>Titre de la fenêtre (barre des tâches) : celui de l'onglet affiché.</summary>
    public string WindowTitle => Selected.WindowTitle;

    /// <summary>Au moins un document a des modifications non enregistrées.</summary>
    public bool HasUnsavedChanges => Documents.Any(d => d.IsModified);

    /// <summary>Fichiers ouverts (rouverts après une mise à jour), dans l'ordre des onglets.</summary>
    public IReadOnlyList<string> OpenPaths => [.. Documents.Select(d => d.FilePath).OfType<string>()];

    /// <summary>
    /// Ouvre un fichier : l'onglet qui l'affiche déjà s'il y en a un, sinon l'onglet d'accueil affiché, sinon un nouvel
    /// onglet juste après celui-ci. En cas d'échec (fichier illisible, mot de passe refusé), l'onglet créé disparaît.
    /// </summary>
    public async Task<bool> OpenFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (Documents.FirstOrDefault(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Selected = open;
            return true;
        }

        var previous = Selected;
        var document = previous is { HasDocument: false, IsBusy: false } ? previous : null;
        var created = document is null;
        if (document is null)
        {
            document = _createDocument();
            Documents.Insert(Documents.IndexOf(previous) + 1, document);
            Subscribe(document);
            Selected = document;
        }

        bool opened;
        try
        {
            opened = await document.OpenFileAsync(fullPath);
        }
        catch
        {
            if (created)
            {
                Remove(document, previous);
            }

            throw;
        }

        if (!opened && created)
        {
            Remove(document, previous);
        }

        return opened;
    }

    /// <summary>Ouvre plusieurs fichiers, chacun dans son onglet ; le dernier ouvert est affiché.</summary>
    public async Task<int> OpenFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var count = 0;
        foreach (var path in paths)
        {
            if (await OpenFileAsync(path))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Ferme un onglet après avoir proposé d'enregistrer ses modifications. Le dernier onglet n'est pas retiré : il
    /// revient à l'accueil. Retourne faux si l'utilisateur annule.
    /// </summary>
    public async Task<bool> CloseAsync(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Documents.Contains(document))
        {
            return true;
        }

        if (Documents.Count == 1)
        {
            return await document.CloseAsync();
        }

        if (document.IsBusy)
        {
            return false;
        }

        Selected = document;
        if (!await document.ConfirmCloseAsync())
        {
            return false;
        }

        var index = Documents.IndexOf(document);
        var next = Documents[index == Documents.Count - 1 ? index - 1 : index + 1];
        Remove(document, next);
        _logger.LogInformation("Onglet fermé ({Count} restant(s))", Documents.Count);
        return true;
    }

    /// <summary>
    /// Avant de fermer la fenêtre ou d'installer une mise à jour : propose d'enregistrer chaque document modifié, en
    /// l'affichant. Retourne faux dès que l'utilisateur annule (les onglets restent tels quels).
    /// </summary>
    public async Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var document in Documents.Where(d => d.IsModified).ToList())
        {
            Selected = document;
            if (!await document.ConfirmCloseAsync())
            {
                return false;
            }
        }

        return true;
    }

    public void SelectNext() => SelectAt(Documents.IndexOf(Selected) + 1);

    public void SelectPrevious() => SelectAt(Documents.IndexOf(Selected) - 1);

    public void Dispose()
    {
        foreach (var document in Documents)
        {
            Unsubscribe(document);
            document.Dispose();
        }

        Documents.Clear();
    }

    /// <summary>« Ouvrir » : un ou plusieurs PDF, chacun dans son onglet.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        var paths = await _files.PickPdfsToOpenAsync();
        await OpenFilesAsync(paths);
    }

    [RelayCommand(CanExecute = nameof(CanCloseSelected))]
    private Task CloseSelectedAsync() => CloseAsync(Selected);

    private bool CanCloseSelected() => Documents.Count > 1 || Selected.HasDocument;

    partial void OnSelectedChanged(DocumentViewModel? oldValue, DocumentViewModel newValue)
    {
        // Un onglet retiré n'est jamais affiché : la sélection revient à un onglet présent.
        if (!Documents.Contains(newValue) && Documents.Count > 0)
        {
            Selected = Documents[0];
        }
    }

    private void SelectAt(int index)
    {
        if (Documents.Count > 1)
        {
            Selected = Documents[(index + Documents.Count) % Documents.Count];
        }
    }

    private void Remove(DocumentViewModel document, DocumentViewModel next)
    {
        if (ReferenceEquals(Selected, document))
        {
            Selected = next;
        }

        Documents.Remove(document);
        Unsubscribe(document);
        document.Dispose();
        CloseSelectedCommand.NotifyCanExecuteChanged();
    }

    private void Subscribe(DocumentViewModel document) => document.PropertyChanged += OnDocumentPropertyChanged;

    private void Unsubscribe(DocumentViewModel document) => document.PropertyChanged -= OnDocumentPropertyChanged;

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Selected) && e.PropertyName == nameof(DocumentViewModel.WindowTitle))
        {
            OnPropertyChanged(nameof(WindowTitle));
        }

        if (e.PropertyName == nameof(DocumentViewModel.HasDocument))
        {
            CloseSelectedCommand.NotifyCanExecuteChanged();
        }
    }
}
