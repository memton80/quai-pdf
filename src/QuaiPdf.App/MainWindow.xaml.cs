using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuaiPdf.App.Localization;
using QuaiPdf.App.Services;
using QuaiPdf.App.Views;
using QuaiPdf.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace QuaiPdf.App;

/// <summary>Fenêtre principale : onglets dans la barre de titre, bandeau de mise à jour, un document par onglet.</summary>
public sealed partial class MainWindow : Window
{
    // Espace laissé libre à droite des onglets pour saisir la fenêtre, en plus des boutons de la barre de titre.
    private const double DragSpace = 48;

    private readonly ILogger<MainWindow> _logger;
    private readonly Dictionary<DocumentViewModel, DocumentView> _views = [];

    // Fermeture : question en cours, puis fermeture accordée (modifications enregistrées ou abandonnées).
    private bool _closeRequested;
    private bool _closeAllowed;

    // Sélection de l'onglet posée par le code : ne pas la renvoyer au view-model.
    private bool _syncingTabs;

    public MainWindow()
    {
        var services = App.Current.Services;
        Workspace = services.GetRequiredService<WorkspaceViewModel>();
        Updates = services.GetRequiredService<UpdateBannerViewModel>();
        _logger = services.GetRequiredService<ILogger<MainWindow>>();
        InitializeComponent();

        Title = Workspace.WindowTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPresenterChange)
            {
                UpdateTitleBarInsets();
            }
        };
        RootGrid.Loaded += (_, _) => UpdateTitleBarInsets();

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "QuaiPdf.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // Les boîtes de dialogue et sélecteurs de fichiers ont besoin de la fenêtre.
        services.GetRequiredService<DialogService>().Root = RootGrid;
        services.GetRequiredService<FileDialogs>().WindowId = AppWindow.Id;
        services.GetRequiredService<ThemeService>().Attach(this, RootGrid);

        Workspace.Documents.CollectionChanged += OnDocumentsChanged;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        SyncViews();
        ShowSelected();
        Tabs.Loaded += (_, _) => SelectTab(Workspace.Selected);

        AddShortcuts();
        AppWindow.Closing += OnClosing;
        ResizeForDpi(1240, 860);
    }

    public WorkspaceViewModel Workspace { get; }

    /// <summary>Bandeau « nouvelle version disponible ».</summary>
    public UpdateBannerViewModel Updates { get; }

    /// <summary>Contenu racine de la fenêtre (test de fumée).</summary>
    public FrameworkElement Root => RootGrid;

    /// <summary>Vue de l'onglet affiché.</summary>
    internal DocumentView View => _views[Workspace.Selected];

    /// <summary>Onglet sélectionné dans la barre de titre (test de fumée).</summary>
    internal object? SelectedTab => Tabs.SelectedItem;

    // ----- Onglets -----

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncViews();
        ShowSelected();
    }

    /// <summary>Une vue par document ouvert : créée avec son onglet, retirée avec lui.</summary>
    private void SyncViews()
    {
        foreach (var (document, view) in _views.Where(p => !Workspace.Documents.Contains(p.Key)).ToList())
        {
            view.Detach();
            DocumentsHost.Children.Remove(view);
            _views.Remove(document);
        }

        foreach (var document in Workspace.Documents.Where(d => !_views.ContainsKey(d)))
        {
            var view = new DocumentView(document) { Visibility = Visibility.Collapsed };
            _views[document] = view;
            DocumentsHost.Children.Add(view);
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceViewModel.Selected):
                ShowSelected();
                break;
            case nameof(WorkspaceViewModel.WindowTitle):
                Title = Workspace.WindowTitle;
                break;
        }
    }

    /// <summary>Seule la vue de l'onglet sélectionné est visible ; les autres gardent leur position de lecture.</summary>
    private void ShowSelected()
    {
        var selected = Workspace.Selected;
        foreach (var (document, view) in _views)
        {
            view.Visibility = ReferenceEquals(document, selected) ? Visibility.Visible : Visibility.Collapsed;
        }

        Title = Workspace.WindowTitle;
        SelectTab(selected);
    }

    private void SelectTab(DocumentViewModel document)
    {
        if (ReferenceEquals(Tabs.SelectedItem, document))
        {
            return;
        }

        _syncingTabs = true;
        try
        {
            Tabs.SelectedItem = document;
        }
        finally
        {
            _syncingTabs = false;
        }
    }

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs)
        {
            return;
        }

        if (Tabs.SelectedItem is DocumentViewModel document && Workspace.Documents.Contains(document))
        {
            Workspace.Selected = document;
        }
        else
        {
            // Onglet retiré par le TabView lui-même (ou aucune sélection) : on remet celle du view-model.
            DispatcherQueue.TryEnqueue(() => SelectTab(Workspace.Selected));
        }
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        try
        {
            if (args.Item is DocumentViewModel document)
            {
                await Workspace.CloseAsync(document);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fermeture de l'onglet interrompue");
        }
    }

    private async void OnAddTabButtonClick(TabView sender, object args)
    {
        try
        {
            await Workspace.OpenCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ouverture interrompue");
        }
    }

    /// <summary>
    /// Barre de titre : les onglets commencent après la zone réservée par Windows à gauche, et le pied de la bande
    /// laisse la place des boutons Réduire / Agrandir / Fermer, plus de quoi saisir la fenêtre.
    /// </summary>
    private void UpdateTitleBarInsets()
    {
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
        if (scale <= 0)
        {
            scale = 1;
        }

        var titleBar = AppWindow.TitleBar;
        Tabs.Margin = new Thickness(titleBar.LeftInset / scale, 0, 0, 0);
        DragRegion.MinWidth = (titleBar.RightInset / scale) + DragSpace;
    }

    // ----- Raccourcis clavier -----

    /// <summary>
    /// Raccourcis de la fenêtre, appliqués à l'onglet affiché. Dans un champ de saisie (recherche, numéro de page),
    /// Ctrl+A, Ctrl+C, Ctrl+Z et Ctrl+Y restent ceux du champ.
    /// </summary>
    private void AddShortcuts()
    {
        Add(VirtualKey.O, VirtualKeyModifiers.Control, () => Workspace.OpenCommand);
        Add(VirtualKey.T, VirtualKeyModifiers.Control, () => Workspace.OpenCommand);
        Add(VirtualKey.W, VirtualKeyModifiers.Control, () => Workspace.CloseSelectedCommand);
        Add(VirtualKey.S, VirtualKeyModifiers.Control, () => Workspace.Selected.SaveCommand);
        Add(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => Workspace.Selected.SaveAsCommand);
        Add(VirtualKey.Z, VirtualKeyModifiers.Control, () => Workspace.Selected.UndoCommand, textInput: false);
        Add(VirtualKey.Y, VirtualKeyModifiers.Control, () => Workspace.Selected.RedoCommand, textInput: false);
        Add(VirtualKey.R, VirtualKeyModifiers.Control, () => Workspace.Selected.RotateRightCommand);
        Add(VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => Workspace.Selected.RotateLeftCommand);
        Add(VirtualKey.I, VirtualKeyModifiers.Control, () => Workspace.Selected.InsertCommand);
        Add(VirtualKey.E, VirtualKeyModifiers.Control, () => Workspace.Selected.ExtractCommand);
        Add(VirtualKey.D, VirtualKeyModifiers.Control, () => Workspace.Selected.EditPropertiesCommand);
        Add((VirtualKey)0xBB, VirtualKeyModifiers.Control, () => Workspace.Selected.ZoomInCommand); // « = / + » du clavier principal
        Add(VirtualKey.Add, VirtualKeyModifiers.Control, () => Workspace.Selected.ZoomInCommand);
        Add((VirtualKey)0xBD, VirtualKeyModifiers.Control, () => Workspace.Selected.ZoomOutCommand); // « - » du clavier principal
        Add(VirtualKey.Subtract, VirtualKeyModifiers.Control, () => Workspace.Selected.ZoomOutCommand);
        Add(VirtualKey.Number0, VirtualKeyModifiers.Control, () => Workspace.Selected.ActualSizeCommand);
        Add(VirtualKey.NumberPad0, VirtualKeyModifiers.Control, () => Workspace.Selected.ActualSizeCommand);
        Add(VirtualKey.C, VirtualKeyModifiers.Control, () => Workspace.Selected.CopyTextCommand, textInput: false);
        Add(VirtualKey.A, VirtualKeyModifiers.Control, () => Workspace.Selected.SelectAllTextCommand, textInput: false);
        Add(VirtualKey.F3, VirtualKeyModifiers.None, () => Workspace.Selected.NextSearchResultCommand);
        Add(VirtualKey.F3, VirtualKeyModifiers.Shift, () => Workspace.Selected.PreviousSearchResultCommand);
        AddAction(VirtualKey.F, VirtualKeyModifiers.Control, () => View.FocusSearch());
        AddAction(VirtualKey.Tab, VirtualKeyModifiers.Control, Workspace.SelectNext);
        AddAction(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, Workspace.SelectPrevious);
        AddAction(VirtualKey.PageDown, VirtualKeyModifiers.Control, Workspace.SelectNext);
        AddAction(VirtualKey.PageUp, VirtualKeyModifiers.Control, Workspace.SelectPrevious);

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Func<ICommand> command, bool textInput = true) =>
            AddAction(key, modifiers, () =>
            {
                var target = command();
                if (target.CanExecute(null))
                {
                    target.Execute(null);
                }
            }, textInput);

        void AddAction(VirtualKey key, VirtualKeyModifiers modifiers, Action action, bool textInput = true)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                // Dans un champ de saisie, le raccourci appartient au champ (copier son texte, l'annuler…).
                if (!textInput && IsTyping())
                {
                    return;
                }

                args.Handled = true;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Raccourci {Key} interrompu", key);
                }
            };
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }
    }

    private bool IsTyping() =>
        RootGrid.XamlRoot is { } root
        && FocusManager.GetFocusedElement(root) is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox;

    // ----- Fichiers déposés depuis l'Explorateur -----

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Drop_Open");
        }
    }

    /// <summary>PDF déposés : chacun dans son onglet.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>()
                .Where(f => string.Equals(Path.GetExtension(f.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Path)
                .ToList();
            await Workspace.OpenFilesAsync(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ouverture des fichiers déposés impossible");
        }
    }

    // ----- Fermeture et mise à jour -----

    /// <summary>Fermeture de la fenêtre (bouton, Alt+F4) : propose d'enregistrer chaque document modifié.</summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeAllowed || !Workspace.HasUnsavedChanges)
        {
            return;
        }

        args.Cancel = true;
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        try
        {
            if (!await Workspace.ConfirmCloseAllAsync())
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // Quitter reste possible même si la question n'a pas pu être posée.
            _logger.LogError(ex, "Question avant fermeture impossible");
        }
        finally
        {
            _closeRequested = false;
        }

        _closeAllowed = true;
        Close();
    }

    /// <summary>Fenêtre ramenée au premier plan (restaurée si elle était réduite).</summary>
    internal void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>
    /// Fermeture pour installer une mise à jour : les documents ont déjà été proposés à l'enregistrement,
    /// l'installateur attend que Quai PDF se termine puis le relance.
    /// </summary>
    internal void CloseForUpdate()
    {
        _closeAllowed = true;
        Close();
    }

    /// <summary>Bouton du bandeau : mettre à jour (ou ouvrir la page de la release), ou annuler le téléchargement.</summary>
    private async void OnUpdateActionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Updates.ActAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mise à jour interrompue");
        }
    }

    private void ResizeForDpi(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }
}
