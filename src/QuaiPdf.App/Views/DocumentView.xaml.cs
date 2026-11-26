using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using QuaiPdf.App.Localization;
using QuaiPdf.App.Services;
using QuaiPdf.Core.Models;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace QuaiPdf.App.Views;

/// <summary>
/// Un onglet : barre de commandes, rail et volets (vignettes, plan, recherche, propriétés), pages et barre d'état.
/// Les images des pages sont dessinées à la demande, seulement pour les pages à l'écran.
/// </summary>
public sealed partial class DocumentView : UserControl
{
    // Hauteur de l'écran au-dessus d'une occurrence montrée (elle n'arrive pas collée au bord).
    private const double RevealRatio = 0.3;

    // Délai avant de redessiner les pages après un changement de zoom (le zoom au clavier ou à la molette se répète).
    private static readonly TimeSpan ZoomRenderDelay = TimeSpan.FromMilliseconds(150);

    // Espace entre deux pages (PageSpacing dans Styles/Common.xaml).
    private const double PageSpacing = 16;

    // Pages à l'écran (ou juste autour), avec leur indice.
    private readonly Dictionary<FrameworkElement, int> _realized = [];
    private readonly ILogger<DocumentView> _logger;
    private readonly DispatcherQueueTimer _zoomTimer;

    // La sélection des vignettes est modifiée par le code (après une modification) : ne pas la renvoyer au view-model.
    private bool _syncingSelection;

    // Défilement demandé par le code (aller à une page) : position visée. Tant que la lecture y est, la page visible
    // est celle demandée (la dernière page, trop courte pour monter en haut de l'écran, n'atteindrait jamais le
    // repère). Une position et non un drapeau : si la page est déjà là, aucun ViewChanged ne vient l'effacer, et le
    // défilement suivant de l'utilisateur, ailleurs, doit compter.
    private double? _scrollTarget;

    // Écart toléré entre la position visée et la position atteinte (arrondis de mise à l'échelle).
    private const double ScrollTolerance = 1;

    // Bouton de la souris enfoncé sur une page : le glisser étend la sélection de texte.
    private bool _selectingText;

    // Défilement automatique quand le glisser sort de la zone de lecture (pixels par mouvement de souris).
    private const double DragScrollStep = 24;

    // Volet choisi dans le rail (sa visibilité est le réglage commun ShowThumbnails).
    private SidePanel _panel;

    // Dernière recherche lancée : Entrée de nouveau sur le même texte passe au résultat suivant.
    private string? _lastSearch;

    // Zones de texte : déplacement en cours, zone survolée, éditeur ouvert.
    private TextBoxDrag? _drag;
    private PdfTextBox? _hover;
    private TextBoxEditor? _editor;

    // Un glisser plus court que ceci est un simple clic (la zone est sélectionnée, pas déplacée).
    private const double DragThreshold = 3;

    /// <summary>Vue de l'onglet affiché (nécessaire au compilateur XAML ; la fenêtre passe toujours le document).</summary>
    public DocumentView()
        : this(App.Current.Services.GetRequiredService<WorkspaceViewModel>().Selected)
    {
    }

    public DocumentView(DocumentViewModel viewModel)
    {
        var services = App.Current.Services;
        ViewModel = viewModel;
        Workspace = services.GetRequiredService<WorkspaceViewModel>();
        Settings = services.GetRequiredService<SettingsViewModel>();
        Dialogs = services.GetRequiredService<DialogService>();
        _logger = services.GetRequiredService<ILogger<DocumentView>>();
        InitializeComponent();

        _zoomTimer = DispatcherQueue.CreateTimer();
        _zoomTimer.Interval = ZoomRenderDelay;
        _zoomTimer.IsRepeating = false;
        _zoomTimer.Tick += (_, _) => RenderVisiblePages();

        ViewModel.ContentChanged += OnContentChanged;
        ViewModel.ScrollRequested += (_, index) => ScrollToPage(index);
        ViewModel.RevealRequested += (_, e) => RevealArea(e.Page, e.Area);
        ViewModel.SelectionFollowedPage += (_, _) => OnSelectionFollowedPage();
        ViewModel.TextSelectionChanged += (_, _) => DrawVisibleSelections();
        ViewModel.TextBoxesChanged += (_, _) => DrawVisibleTextBoxes();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        Scroller.SizeChanged += (_, e) => ViewModel.ViewportWidth = e.NewSize.Width;
        Scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnScrollerPointerWheelChanged), handledEventsToo: true);
        ShowPanel(Settings.SidePanel);
    }

    public DocumentViewModel ViewModel { get; }

    public WorkspaceViewModel Workspace { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Volet choisi dans le rail (test de fumée).</summary>
    internal SidePanel ActivePanel => _panel;

    /// <summary>Entrées du plan affichées (test de fumée).</summary>
    internal int OutlineItemCount => (OutlineTree.ItemsSource as IReadOnlyCollection<OutlineItemViewModel>)?.Count ?? 0;

    /// <summary>Vue retirée de la fenêtre (onglet fermé) : elle ne suit plus les réglages communs.</summary>
    public void Detach()
    {
        _ = CloseTextBoxEditorAsync(cancel: true);
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _zoomTimer.Stop();
    }

    /// <summary>Ctrl+F : le curseur va dans le champ de recherche.</summary>
    public void FocusSearch()
    {
        if (ViewModel.HasDocument)
        {
            SearchBox.Focus(FocusState.Keyboard);
        }
    }

    /// <summary>Ouvre un volet (rail, Ctrl+F, test de fumée).</summary>
    public void OpenPanel(SidePanel panel)
    {
        Settings.SidePanel = panel;
        Settings.ShowThumbnails = true;
        ShowPanel(panel);
    }

    private DialogService Dialogs { get; }

    /// <summary>Numéro affiché dans la barre d'outils (test de fumée).</summary>
    internal int ShownPageNumber => (int)Math.Round(PageBox.Value);

    /// <summary>Bandeau « Lecture seule » visible (test de fumée).</summary>
    internal bool ReadOnlyBannerShown => ReadOnlyBar.IsOpen && ReadOnlyBar.Visibility == Visibility.Visible;

    /// <summary>Nombre de pages dessinées à l'écran (test de fumée).</summary>
    internal int RenderedPageCount => _realized.Keys.Count(e => PageImage(e) is { Source: not null });

    /// <summary>Rectangles de surlignage du texte à l'écran (test de fumée).</summary>
    internal int HighlightCount => _realized.Keys.Sum(e => TextLayer(e)?.Children.Count ?? 0);

    /// <summary>Vignettes sélectionnées, dans l'ordre (test de fumée).</summary>
    internal IReadOnlyList<int> SelectedThumbnails =>
        [.. Thumbnails.SelectedRanges.SelectMany(range => Enumerable.Range(range.FirstIndex, (int)range.Length)).Order()];

    /// <summary>Fait défiler la lecture tout en bas, comme l'utilisateur (test de fumée).</summary>
    internal void ScrollReaderToEnd() => Scroller.ChangeView(null, Scroller.ScrollableHeight, null, disableAnimation: true);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentViewModel.Zoom):
                OnZoomChanged();
                break;
            case nameof(DocumentViewModel.CurrentSearchResult):
                SyncSearchResult();
                break;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ShowThumbnails))
        {
            UpdateRail();
        }
    }

    /// <summary>Nouveau contenu : pages redessinées, sélection et position rétablies.</summary>
    private void OnContentChanged(object? sender, DocumentChangedEventArgs e)
    {
        // Un souci d'affichage (sélection, défilement) ne doit jamais faire échouer l'ouverture ou la modification.
        try
        {
            ShowChange(e);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mise à jour de l'affichage incomplète");
        }
    }

    private void ShowChange(DocumentChangedEventArgs e)
    {
        SelectThumbnails(e.Selection);
        RenderVisiblePages();
        DrawVisibleTextBoxes();
        RenderVisibleThumbnails();
        RefreshPanel();

        if (e.ScrollTo is { } page)
        {
            ScrollToPage(page);
            if (page < ViewModel.Pages.Count)
            {
                Thumbnails.ScrollIntoView(ViewModel.Pages[page]);
            }
        }
    }

    private void OnZoomChanged()
    {
        // La page en haut de l'écran le reste après le changement de zoom.
        var anchor = ViewModel.CurrentPageNumber - 1;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ScrollToPage(anchor));
        _zoomTimer.Stop();
        _zoomTimer.Start();
    }

    private void ScrollToPage(int index)
    {
        if (index < 0 || index >= ViewModel.Pages.Count)
        {
            return;
        }

        // La page demandée est la page visible (après un zoom, la mise en page a pu en annoncer une autre entre-temps).
        if (ViewModel.CurrentPageNumber != index + 1)
        {
            ViewModel.CurrentPageNumber = index + 1;
        }

        try
        {
            PagesRepeater.UpdateLayout();
            var element = PagesRepeater.GetOrCreateElement(index);
            element.UpdateLayout();

            // Haut de la page en haut de l'écran, dans la limite du défilement possible (dernières pages).
            var top = element.TransformToVisual(Scroller).TransformPoint(default).Y;
            _scrollTarget = Math.Clamp(Scroller.VerticalOffset + top, 0, Scroller.ScrollableHeight);
            element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        }
        catch (Exception ex)
        {
            _scrollTarget = null;
            _logger.LogWarning(ex, "Défilement jusqu'à la page {Page} impossible", index + 1);
        }
    }

    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollTarget is { } target)
        {
            if (Math.Abs(Scroller.VerticalOffset - target) <= ScrollTolerance)
            {
                // Arrivé à la page demandée : elle est déjà la page visible.
                if (!e.IsIntermediate)
                {
                    _scrollTarget = null;
                }

                return;
            }

            // L'utilisateur fait défiler ailleurs (ou la page demandée était déjà à l'écran).
            _scrollTarget = null;
        }

        // Pendant le défilement aussi (molette, glisser de la barre) : le numéro et la vignette suivent en direct.
        UpdateVisiblePage();
    }

    /// <summary>
    /// Page visible : celle qui occupe le premier quart de l'écran ; tout en bas du document, la dernière page (trop
    /// courte, elle n'atteindrait jamais ce repère).
    /// </summary>
    private void UpdateVisiblePage()
    {
        int? visible = null;
        if (Scroller.ScrollableHeight > 0 && Scroller.VerticalOffset >= Scroller.ScrollableHeight - ScrollTolerance)
        {
            visible = ViewModel.Pages.Count - 1;
        }
        else
        {
            var probe = Scroller.ViewportHeight / 4;
            foreach (var (element, index) in _realized)
            {
                var top = element.TransformToVisual(Scroller).TransformPoint(default).Y;
                if (top <= probe && top + element.ActualHeight + PageSpacing > probe)
                {
                    visible = index;
                    break;
                }
            }
        }

        if (visible is { } page && page >= 0 && ViewModel.CurrentPageNumber != page + 1)
        {
            ViewModel.CurrentPageNumber = page + 1;
        }
    }

    private void OnScrollerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) == 0 || !ViewModel.HasDocument)
        {
            return;
        }

        var delta = e.GetCurrentPoint(Scroller).Properties.MouseWheelDelta;
        var command = delta > 0 ? ViewModel.ZoomInCommand : ViewModel.ZoomOutCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    private void OnPageBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue))
        {
            sender.Value = ViewModel.CurrentPageNumber;
            return;
        }

        var number = (int)Math.Round(args.NewValue);
        if (number != ViewModel.CurrentPageNumber)
        {
            ViewModel.GoToPage(number);
        }

        // Numéro hors du document (ou aucun document) : la case reprend la page affichée.
        if ((int)Math.Round(sender.Value) != ViewModel.CurrentPageNumber)
        {
            sender.Value = ViewModel.CurrentPageNumber;
        }
    }

    private void OnZoomPresetClick(object sender, RoutedEventArgs e) =>
        ViewModel.Zoom = UiHelpers.ParseZoom((sender as FrameworkElement)?.Tag);

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Dialogs.ShowSettingsAsync(Settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Réglages interrompus");
        }
    }

    // ----- Pages -----

    private void OnPageElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            _realized[element] = args.Index;
            _ = RenderPageAsync(element, args.Index);
            DrawSelection(element, args.Index);
            DrawTextBoxes(element, args.Index);
        }
    }

    private void OnPageElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            _realized[element] = args.NewIndex;
            _ = RenderPageAsync(element, args.NewIndex);
            DrawSelection(element, args.NewIndex);
            DrawTextBoxes(element, args.NewIndex);
        }
    }

    private void OnPageElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            // Page qui quitte l'écran avec son éditeur ouvert : le texte tapé est gardé.
            if (_editor?.Element == element)
            {
                _ = CloseTextBoxEditorAsync();
            }

            _realized.Remove(element);
            element.Tag = null;
            AnnotationLayer(element)?.Children.Clear();
            if (PageImage(element) is { } image)
            {
                image.Source = null;
            }

            TextLayer(element)?.Children.Clear();
        }
    }

    private void RenderVisiblePages()
    {
        foreach (var (element, index) in _realized.ToList())
        {
            _ = RenderPageAsync(element, index);
        }
    }

    private async Task RenderPageAsync(FrameworkElement element, int index)
    {
        if (PageImage(element) is not { } image
            || ViewModel.Rendered is not WindowsRenderedPdf rendered
            || index >= ViewModel.Pages.Count)
        {
            return;
        }

        // Jeton : seule la dernière demande pour cet élément affiche son image.
        var token = new object();
        element.Tag = token;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        try
        {
            var bitmap = await rendered.RenderAsync(index, ViewModel.Pages[index].DisplayWidth * scale);
            if (bitmap is not null && ReferenceEquals(element.Tag, token))
            {
                image.Source = bitmap;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Page {Page} non dessinée", index + 1);
        }
    }

    // Une page : Border > TextPageSurface > [Image, Viewbox > Canvas] (voir le modèle dans DocumentView.xaml).
    private static Image? PageImage(FrameworkElement element) =>
        element is Border { Child: TextPageSurface surface } && surface.Children.Count > 0 ? surface.Children[0] as Image : null;

    private static Canvas? TextLayer(FrameworkElement element) =>
        element is Border { Child: TextPageSurface surface } && surface.Children.Count > 1 && surface.Children[1] is Viewbox { Child: Canvas canvas }
            ? canvas
            : null;

    // Une page : Border > TextPageSurface > [Image, Viewbox > Canvas (surlignage), Viewbox > Canvas (zones de texte)].
    private static Canvas? AnnotationLayer(FrameworkElement element) =>
        element is Border { Child: TextPageSurface surface } && surface.Children.Count > 2 && surface.Children[2] is Viewbox { Child: Canvas canvas }
            ? canvas
            : null;

    // ----- Texte : sélection à la souris, surlignage -----

    private void OnPagesPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PagesRepeater);
        if (!point.Properties.IsLeftButtonPressed || !ViewModel.HasDocument)
        {
            // Clic droit : la sélection reste, le menu « Copier » s'ouvre.
            return;
        }

        // Le clavier suit la lecture (Ctrl+C, Ctrl+A, Échap, Suppr), même si un champ avait le focus. Un éditeur de zone
        // de texte ouvert perd le focus : son texte est enregistré.
        Scroller.Focus(FocusState.Pointer);
        if (PageUnder(e) is not { } hit)
        {
            ViewModel.ClearTextSelection();
            return;
        }

        if (PressTextBox(hit.Index, hit.Point, e))
        {
            e.Handled = true;
            return;
        }

        var extend = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        if (ViewModel.BeginTextSelection(hit.Index, hit.Point, extend))
        {
            _selectingText = PagesRepeater.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPagesPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not null)
        {
            DragTextBox(e);
            e.Handled = true;
            return;
        }

        if (!_selectingText)
        {
            HoverTextBox(e);
            return;
        }

        // Glisser au-delà du haut ou du bas de la zone de lecture : les pages défilent.
        var inReader = e.GetCurrentPoint(Scroller).Position.Y;
        if (inReader < 0 || inReader > Scroller.ViewportHeight)
        {
            var step = inReader < 0 ? -DragScrollStep : DragScrollStep;
            Scroller.ChangeView(null, Scroller.VerticalOffset + step, null, disableAnimation: true);
        }

        if (PageUnder(e) is { } hit)
        {
            ViewModel.ExtendTextSelection(hit.Index, hit.Point);
        }

        e.Handled = true;
    }

    private void OnPagesPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is { } drag)
        {
            _drag = null;
            PagesRepeater.ReleasePointerCapture(e.Pointer);
            if (drag.Moved && drag.Target is { } target)
            {
                _ = MoveTextBoxAsync(drag.Box, target);
            }
            else
            {
                DrawVisibleTextBoxes();
            }

            return;
        }

        if (_selectingText)
        {
            _selectingText = false;
            PagesRepeater.ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnPagesDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (PageUnder(e.GetPosition) is { } hit)
        {
            // Double-clic sur une zone de texte : on la modifie ; ailleurs, on sélectionne un mot.
            if (ViewModel.CanEdit && ViewModel.TextBoxAt(hit.Index, hit.Point) is { CanEditText: true } box)
            {
                OpenTextBoxEditor(box.Page, box, new PdfPoint(box.Area.X, box.Area.Y));
            }
            else
            {
                ViewModel.SelectWordAt(hit.Index, hit.Point);
            }

            e.Handled = true;
        }
    }

    private void OnScrollerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var box = ViewModel.SelectedTextBox;
        switch (e.Key)
        {
            case VirtualKey.Delete when box is { CanDelete: true } && ViewModel.CanEdit:
                _ = ViewModel.DeleteSelectedTextBoxCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case VirtualKey.Enter or VirtualKey.F2 when box is { CanEditText: true } && ViewModel.CanEdit:
                OpenTextBoxEditor(box.Page, box, new PdfPoint(box.Area.X, box.Area.Y));
                e.Handled = true;
                break;
            case VirtualKey.Escape when box is not null:
                ViewModel.SelectTextBox(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape when ViewModel.IsTextToolActive:
                ViewModel.IsTextToolActive = false;
                e.Handled = true;
                break;
            case VirtualKey.Escape when ViewModel.HasTextSelection:
                ViewModel.ClearTextSelection();
                e.Handled = true;
                break;
        }
    }

    private (int Index, PdfPoint Point)? PageUnder(PointerRoutedEventArgs e) =>
        PageUnder(element => e.GetCurrentPoint(element).Position);

    /// <summary>
    /// Page sous le pointeur (ou la plus proche, entre deux pages), et le point sur cette page en pixels à 100 %.
    /// </summary>
    private (int Index, PdfPoint Point)? PageUnder(Func<UIElement, Windows.Foundation.Point> position)
    {
        (int Index, FrameworkElement Element, double Distance)? best = null;
        foreach (var (element, index) in _realized)
        {
            var y = position(element).Y;
            var distance = y < 0 ? -y : y > element.ActualHeight ? y - element.ActualHeight : 0;
            if (best is null || distance < best.Value.Distance)
            {
                best = (index, element, distance);
            }
        }

        if (best is not { } page || TextLayer(page.Element) is not { } layer || page.Index >= ViewModel.Pages.Count)
        {
            return null;
        }

        // Repère du surlignage : la page à 100 %, quelle que soit la mise à l'échelle.
        var size = ViewModel.Pages[page.Index].Size;
        var point = position(layer);
        return (page.Index, new PdfPoint(Math.Clamp(point.X, 0, size.Width), Math.Clamp(point.Y, 0, size.Height)));
    }

    private void DrawVisibleSelections()
    {
        foreach (var (element, index) in _realized)
        {
            DrawSelection(element, index);
        }
    }

    private void DrawSelection(FrameworkElement element, int index)
    {
        if (TextLayer(element) is not { } layer)
        {
            return;
        }

        layer.Children.Clear();
        try
        {
            var style = (Style)Application.Current.Resources["TextHighlightStyle"];
            foreach (var rect in ViewModel.TextSelectionRects(index))
            {
                var highlight = new Rectangle { Width = rect.Width, Height = rect.Height, Style = style };
                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop(highlight, rect.Y);
                layer.Children.Add(highlight);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Surlignage de la page {Page} impossible", index + 1);
        }
    }

    // ----- Vignettes -----

    /// <summary>Sélection des vignettes posée par le code (elle n'est pas renvoyée au view-model).</summary>
    private void SelectThumbnails(IReadOnlyList<int> selection)
    {
        _syncingSelection = true;
        try
        {
            Thumbnails.SelectedItems.Clear();
            foreach (var index in selection)
            {
                if (index >= 0 && index < ViewModel.Pages.Count)
                {
                    Thumbnails.SelectRange(new ItemIndexRange(index, 1));
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// La page visible a changé et la sélection d'une page l'a suivie : la barre de sélection passe sur sa vignette,
    /// qui défile à l'écran si besoin.
    /// </summary>
    private void OnSelectionFollowedPage()
    {
        try
        {
            SelectThumbnails(ViewModel.SelectedIndices);
            if (ViewModel.SelectedIndices is [var index] && index < ViewModel.Pages.Count)
            {
                Thumbnails.ScrollIntoView(ViewModel.Pages[index]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vignette de la page visible non sélectionnée");
        }
    }

    private void OnThumbnailContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            args.ItemContainer.Tag = null;
            if (ThumbnailImage(args.ItemContainer) is { } image)
            {
                image.Source = null;
            }

            return;
        }

        AutomationProperties.SetName(args.ItemContainer, UiHelpers.PageName(args.ItemIndex + 1));
        _ = RenderThumbnailAsync(args.ItemContainer, args.ItemIndex);
    }

    private void RenderVisibleThumbnails()
    {
        for (var i = 0; i < ViewModel.Pages.Count; i++)
        {
            if (Thumbnails.ContainerFromIndex(i) is SelectorItem container)
            {
                AutomationProperties.SetName(container, UiHelpers.PageName(i + 1));
                _ = RenderThumbnailAsync(container, i);
            }
        }
    }

    private async Task RenderThumbnailAsync(SelectorItem container, int index)
    {
        if (ThumbnailImage(container) is not { } image || ViewModel.Rendered is not WindowsRenderedPdf rendered)
        {
            return;
        }

        var token = new object();
        container.Tag = token;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        try
        {
            var bitmap = await rendered.RenderAsync(index, PageItemViewModel.ThumbnailWidth * scale);
            if (bitmap is not null && ReferenceEquals(container.Tag, token))
            {
                image.Source = bitmap;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vignette {Page} non dessinée", index + 1);
        }
    }

    private static Image? ThumbnailImage(SelectorItem container) =>
        container.ContentTemplateRoot is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is Border { Child: Image image }
            ? image
            : null;

    private void OnThumbnailsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        var indices = Thumbnails.SelectedRanges
            .SelectMany(range => Enumerable.Range(range.FirstIndex, (int)range.Length))
            .ToList();
        ViewModel.SetSelection(indices);

        // Un clic sur une vignette montre la page.
        if (e.AddedItems is [PageItemViewModel page])
        {
            ViewModel.GoToPage(page.Number);
        }
    }

    private void OnThumbnailsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && ViewModel.DeleteCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.DeleteCommand.Execute(null);
        }
        else if (e.Key == VirtualKey.Escape && Thumbnails.SelectedItems.Count > 0)
        {
            e.Handled = true;
            Thumbnails.SelectedItems.Clear();
        }
    }

    private void OnThumbnailsDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Les pages glissées deviennent la sélection : elle les suit à leur nouvelle place.
        ViewModel.SetSelection(e.Items.OfType<PageItemViewModel>().Select(p => p.Index));
    }

    private async void OnThumbnailsDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.Move)
        {
            return;
        }

        try
        {
            await ViewModel.ReorderAsync(ViewModel.Pages.Select(p => p.Index).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Réordonnancement des pages interrompu");
        }
    }

    // ----- Rail et volets -----

    private void OnRailClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || !Enum.TryParse<SidePanel>(tag, out var panel))
        {
            return;
        }

        // Second clic sur le volet ouvert : il se referme (comme la barre d'activité de Visual Studio Code).
        if (Settings.ShowThumbnails && panel == _panel)
        {
            Settings.ShowThumbnails = false;
            UpdateRail();
            return;
        }

        OpenPanel(panel);
    }

    /// <summary>Affiche le contenu du volet choisi et le remplit (plan, propriétés) si besoin.</summary>
    private void ShowPanel(SidePanel panel)
    {
        _panel = Enum.IsDefined(panel) ? panel : SidePanel.Pages;
        Thumbnails.Visibility = UiHelpers.VisibleIf(_panel == SidePanel.Pages);
        OutlinePanel.Visibility = UiHelpers.VisibleIf(_panel == SidePanel.Outline);
        SearchPanel.Visibility = UiHelpers.VisibleIf(_panel == SidePanel.Search);
        PropertiesPanel.Visibility = UiHelpers.VisibleIf(_panel == SidePanel.Properties);
        PanelTitle.Text = Loc.Get(_panel switch
        {
            SidePanel.Outline => "Panel_Outline",
            SidePanel.Search => "Panel_Search",
            SidePanel.Properties => "Panel_Properties",
            _ => "Panel_Pages",
        });
        UpdateRail();
        RefreshPanel();

        if (_panel == SidePanel.Pages)
        {
            RenderVisibleThumbnails();
        }
    }

    /// <summary>Bouton du rail allumé : celui du volet ouvert (aucun si le volet est fermé).</summary>
    private void UpdateRail()
    {
        var open = Settings.ShowThumbnails;
        PagesButton.IsChecked = open && _panel == SidePanel.Pages;
        OutlineButton.IsChecked = open && _panel == SidePanel.Outline;
        SearchButton.IsChecked = open && _panel == SidePanel.Search;
        PropertiesButton.IsChecked = open && _panel == SidePanel.Properties;
    }

    /// <summary>Contenu du volet relu après une ouverture ou une modification (plan, propriétés).</summary>
    private void RefreshPanel()
    {
        try
        {
            switch (_panel)
            {
                case SidePanel.Outline:
                    IReadOnlyList<OutlineItemViewModel> outline = ViewModel.HasDocument ? ViewModel.Outline : [];
                    OutlineTree.ItemsSource = outline;
                    NoOutlineText.Visibility = UiHelpers.VisibleIf(outline.Count == 0);
                    break;
                case SidePanel.Properties:
                    _ = RefreshPropertiesAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Volet {Panel} non rempli", _panel);
        }
    }

    private async Task RefreshPropertiesAsync()
    {
        try
        {
            await ViewModel.RefreshPropertiesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Propriétés du document non affichées");
        }
    }

    private void OnOutlineItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is OutlineItemViewModel item)
        {
            ViewModel.GoToOutlineItem(item);
        }
    }

    // ----- Recherche -----

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try
        {
            var query = args.QueryText.Trim();
            OpenPanel(SidePanel.Search);
            if (query.Length > 0 && query == _lastSearch && ViewModel.HasSearchResults)
            {
                ViewModel.NextSearchResultCommand.Execute(null);
                return;
            }

            ViewModel.SearchQuery = query;
            _lastSearch = query;
            await ViewModel.SearchCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recherche interrompue");
        }
    }

    private void OnSearchResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResultViewModel result)
        {
            ViewModel.ShowSearchResult(result);
        }
    }

    /// <summary>L'occurrence montrée est sélectionnée dans la liste des résultats, qui défile jusqu'à elle.</summary>
    private void SyncSearchResult()
    {
        var current = ViewModel.CurrentSearchResult;
        if (!ReferenceEquals(SearchResultsList.SelectedItem, current))
        {
            SearchResultsList.SelectedItem = current;
        }

        if (current is not null)
        {
            SearchResultsList.ScrollIntoView(current);
        }
    }

    /// <summary>
    /// Montre une zone d'une page (occurrence trouvée) au premier tiers de l'écran, horizontalement aussi quand la
    /// page est plus large que la zone de lecture.
    /// </summary>
    private void RevealArea(int index, PdfRect area)
    {
        if (index < 0 || index >= ViewModel.Pages.Count)
        {
            return;
        }

        if (ViewModel.CurrentPageNumber != index + 1)
        {
            ViewModel.CurrentPageNumber = index + 1;
        }

        try
        {
            PagesRepeater.UpdateLayout();
            var element = PagesRepeater.GetOrCreateElement(index);
            element.UpdateLayout();
            var origin = element.TransformToVisual(Scroller).TransformPoint(default);
            var zoom = ViewModel.Zoom;
            var y = Math.Clamp(
                Scroller.VerticalOffset + origin.Y + (area.Y * zoom) - (Scroller.ViewportHeight * RevealRatio),
                0,
                Scroller.ScrollableHeight);
            var x = Math.Clamp(
                Scroller.HorizontalOffset + origin.X + (area.X * zoom) - (Scroller.ViewportWidth * RevealRatio),
                0,
                Scroller.ScrollableWidth);
            _scrollTarget = y;
            Scroller.ChangeView(x, y, null, disableAnimation: true);
        }
        catch (Exception ex)
        {
            _scrollTarget = null;
            _logger.LogWarning(ex, "Occurrence de la page {Page} non montrée", index + 1);
        }
    }

    // ----- Zoom (barre d'état) -----

    private void OnZoomSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // La valeur suit le zoom (liaison) : seul un geste sur le curseur change le zoom.
        if (ViewModel.HasDocument && Math.Abs((e.NewValue / 100) - ViewModel.Zoom) > 0.005)
        {
            ViewModel.Zoom = e.NewValue / 100;
        }
    }

    // ----- Zones de texte -----

    /// <summary>Zone en cours de déplacement : point de départ, et coin haut gauche visé (pixels à 100 %).</summary>
    private sealed class TextBoxDrag(PdfTextBox box, FrameworkElement element, PdfPoint start)
    {
        public PdfTextBox Box { get; } = box;

        public FrameworkElement Element { get; } = element;

        public PdfPoint Start { get; } = start;

        public bool Moved { get; set; }

        public PdfPoint? Target { get; set; }
    }

    /// <summary>Éditeur posé sur une page : zone modifiée (ou <c>null</c> pour une nouvelle), place, contenu d'origine.</summary>
    private sealed record TextBoxEditor(
        TextBox Box, int Page, PdfTextBox? Existing, PdfPoint TopLeft, FrameworkElement Element, byte[]? Content, PdfTextStyle Style);

    /// <summary>Éditeur de zone de texte ouvert (test de fumée).</summary>
    internal bool IsEditingTextBox => _editor is not null;

    /// <summary>Contours de zones de texte dessinés à l'écran (test de fumée).</summary>
    internal int TextBoxOutlineCount => _realized.Keys.Sum(e => AnnotationLayer(e)?.Children.OfType<Rectangle>().Count() ?? 0);

    /// <summary>Ouvre l'éditeur et y tape un texte, comme l'utilisateur (test de fumée).</summary>
    internal bool TypeInTextBoxEditor(int page, PdfTextBox? existing, PdfPoint topLeft, string text)
    {
        OpenTextBoxEditor(page, existing, topLeft);
        if (_editor is not { } editor)
        {
            return false;
        }

        editor.Box.Text = text;
        return true;
    }

    /// <summary>Ferme l'éditeur en gardant le texte (test de fumée ; d'ordinaire, un clic ailleurs).</summary>
    internal Task CommitTextBoxEditorAsync() => CloseTextBoxEditorAsync();

    /// <summary>
    /// Appui sur une page, avant la sélection de texte : une zone de texte sous le pointeur est sélectionnée (et peut
    /// être glissée) ; avec l'outil « Texte », un clic modifie la zone, ou en pose une nouvelle. Retourne vrai si l'appui
    /// a servi aux zones de texte.
    /// </summary>
    private bool PressTextBox(int page, PdfPoint point, PointerRoutedEventArgs e)
    {
        if (!ViewModel.CanEdit)
        {
            return false;
        }

        if (ViewModel.TextBoxAt(page, point) is { } box)
        {
            ViewModel.ClearTextSelection();
            ViewModel.SelectTextBox(box);
            if (ViewModel.IsTextToolActive && box.CanEditText)
            {
                OpenTextBoxEditor(page, box, new PdfPoint(box.Area.X, box.Area.Y));
            }
            else if (box.CanMove && ElementOf(page) is { } element && PagesRepeater.CapturePointer(e.Pointer))
            {
                _drag = new TextBoxDrag(box, element, point);
            }

            return true;
        }

        ViewModel.SelectTextBox(null);
        if (ViewModel.IsTextToolActive)
        {
            ViewModel.ClearTextSelection();
            OpenTextBoxEditor(page, null, point);
            return true;
        }

        return false;
    }

    private void DragTextBox(PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || AnnotationLayer(drag.Element) is not { } layer)
        {
            return;
        }

        var position = e.GetCurrentPoint(layer).Position;
        var dx = position.X - drag.Start.X;
        var dy = position.Y - drag.Start.Y;
        if (!drag.Moved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        drag.Moved = true;
        drag.Target = new PdfPoint(drag.Box.Area.X + dx, drag.Box.Area.Y + dy);
        DrawTextBoxes(drag.Element, drag.Box.Page);
    }

    /// <summary>Survol : contour léger de la zone, et curseur de déplacement si elle peut bouger.</summary>
    private void HoverTextBox(PointerRoutedEventArgs e)
    {
        PdfTextBox? box = null;
        if (ViewModel.CanEdit && PageUnder(e) is { } hit)
        {
            box = ViewModel.TextBoxAt(hit.Index, hit.Point);
            if (ElementOf(hit.Index) is Border { Child: TextPageSurface surface })
            {
                surface.ShowCursor(box switch
                {
                    null => InputSystemCursorShape.IBeam,
                    { CanMove: true } when !ViewModel.IsTextToolActive => InputSystemCursorShape.SizeAll,
                    { CanEditText: true } when ViewModel.IsTextToolActive => InputSystemCursorShape.IBeam,
                    _ => InputSystemCursorShape.Arrow,
                });
            }
        }

        if (box != _hover)
        {
            _hover = box;
            DrawVisibleTextBoxes();
        }
    }

    private async Task MoveTextBoxAsync(PdfTextBox box, PdfPoint target)
    {
        try
        {
            await ViewModel.MoveTextBoxAsync(box, target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Déplacement de la zone de texte interrompu");
        }
        finally
        {
            DrawVisibleTextBoxes();
        }
    }

    private void DrawVisibleTextBoxes()
    {
        foreach (var (element, index) in _realized)
        {
            DrawTextBoxes(element, index);
        }
    }

    /// <summary>Contours de la page : zone sélectionnée (à sa place visée pendant un glisser) et zone survolée.</summary>
    private void DrawTextBoxes(FrameworkElement element, int index)
    {
        if (AnnotationLayer(element) is not { } layer)
        {
            return;
        }

        for (var i = layer.Children.Count - 1; i >= 0; i--)
        {
            if (layer.Children[i] is not TextBox)
            {
                layer.Children.RemoveAt(i);
            }
        }

        try
        {
            var selected = ViewModel.SelectedTextBox;
            if (selected is not null && selected.Page == index && _editor?.Existing != selected)
            {
                var area = _drag is { Moved: true, Target: { } target } drag && drag.Box == selected
                    ? new PdfRect(target.X, target.Y, selected.Area.Width, selected.Area.Height)
                    : selected.Area;
                AddOutline(layer, area, "TextBoxSelectionStyle");
            }

            if (_hover is { } hover && hover.Page == index && hover != selected && _editor?.Existing != hover)
            {
                AddOutline(layer, hover.Area, "TextBoxHoverStyle");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Contours des zones de texte de la page {Page} non dessinés", index + 1);
        }

        static void AddOutline(Canvas layer, PdfRect area, string style)
        {
            var outline = new Rectangle { Width = area.Width, Height = area.Height, Style = (Style)Application.Current.Resources[style] };
            Canvas.SetLeft(outline, area.X);
            Canvas.SetTop(outline, area.Y);
            layer.Children.Insert(0, outline);
        }
    }

    /// <summary>
    /// Ouvre l'éditeur sur la page : sur une zone existante (son texte, sa taille, sa couleur, au moins sa taille
    /// actuelle pour la cacher), ou pour une nouvelle zone au point cliqué. Entrée va à la ligne ; un clic ailleurs ou
    /// Ctrl+Entrée enregistre ; Échap abandonne.
    /// </summary>
    private void OpenTextBoxEditor(int page, PdfTextBox? existing, PdfPoint topLeft)
    {
        if (!ViewModel.CanEdit)
        {
            return;
        }

        _ = CloseTextBoxEditorAsync();
        if (ElementOf(page) is not { } element || AnnotationLayer(element) is not { } layer)
        {
            return;
        }

        var style = existing is null ? ViewModel.TextStyle : new PdfTextStyle(existing.FontSize, existing.Color);
        var empty = PdfTextBoxes.Measure("M", style.FontSize);
        var box = new TextBox
        {
            Style = (Style)Application.Current.Resources["TextBoxEditorStyle"],
            RequestedTheme = ElementTheme.Light,
            Text = existing?.Text.Replace("\n", "\r", StringComparison.Ordinal) ?? string.Empty,
            FontSize = style.FontSize * PdfPageSize.PixelsPerPoint,
            MinWidth = existing?.Area.Width ?? empty.Width * 4,
            MinHeight = existing?.Area.Height ?? empty.Height,
        };

        // Couleur du texte gardée au survol et pendant la saisie (le modèle du champ la remplacerait sinon).
        var brush = UiHelpers.Brush(style.Color);
        box.Foreground = brush;
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
        {
            box.Resources[key] = brush;
        }

        AutomationProperties.SetName(box, Loc.Get("TextBox_Editor"));
        Canvas.SetLeft(box, topLeft.X - 1);
        Canvas.SetTop(box, topLeft.Y - 1);
        box.LostFocus += (_, _) =>
        {
            if (_editor?.Box == box)
            {
                _ = CloseTextBoxEditorAsync();
            }
        };
        box.PreviewKeyDown += (_, args) =>
        {
            var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (args.Key == VirtualKey.Escape || (args.Key == VirtualKey.Enter && control))
            {
                args.Handled = true;
                _ = CloseTextBoxEditorAsync(cancel: args.Key == VirtualKey.Escape);
                Scroller.Focus(FocusState.Programmatic);
            }
        };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectionStart = box.Text.Length;
        };

        _editor = new TextBoxEditor(box, page, existing, topLeft, element, ViewModel.Content, style);
        layer.Children.Add(box);
        DrawTextBoxes(element, page);
    }

    /// <summary>
    /// Ferme l'éditeur ; sauf abandon, son texte est enregistré dans le document (nouvelle zone, ou zone modifiée ; un
    /// texte vidé supprime la zone). Le texte tapé n'est jamais perdu : si le document a changé entre-temps, la zone est
    /// retrouvée, ou reposée à sa place.
    /// </summary>
    private async Task CloseTextBoxEditorAsync(bool cancel = false)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _editor = null;
        var text = editor.Box.Text;
        (editor.Box.Parent as Panel)?.Children.Remove(editor.Box);
        DrawVisibleTextBoxes();
        if (cancel)
        {
            return;
        }

        try
        {
            // Une opération en cours (enregistrement…) se termine d'abord : la zone passe juste après.
            for (var i = 0; ViewModel.IsBusy && i < 200; i++)
            {
                await Task.Delay(50);
            }

            if (editor.Existing is { } existing)
            {
                var current = ReferenceEquals(ViewModel.Content, editor.Content)
                    ? existing
                    : ViewModel.TextBoxes(existing.Page).FirstOrDefault(b => b.Index == existing.Index && b.Text == existing.Text);
                if (current is not null)
                {
                    await ViewModel.UpdateTextBoxAsync(current, text, editor.Style);
                }
                else if (!string.IsNullOrWhiteSpace(text))
                {
                    await ViewModel.AddTextBoxAsync(existing.Page, editor.TopLeft, text);
                }
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                await ViewModel.AddTextBoxAsync(editor.Page, editor.TopLeft, text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone de texte non enregistrée");
        }
    }

    private FrameworkElement? ElementOf(int page) =>
        page >= 0 && page < ViewModel.Pages.Count ? PagesRepeater.TryGetElement(page) as FrameworkElement : null;

    private async void OnTextSizeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (double.TryParse((sender as FrameworkElement)?.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
            {
                await ViewModel.SetTextSizeAsync(size);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Taille du texte non appliquée");
        }
    }

    private async void OnTextColorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (int.TryParse((sender as FrameworkElement)?.Tag as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < ViewModel.TextColors.Count)
            {
                await ViewModel.SetTextColorAsync(ViewModel.TextColors[index].Color);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couleur du texte non appliquée");
        }
    }
}
