using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using QuaiPdf.App.Localization;
using QuaiPdf.App.Services;
using QuaiPdf.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace QuaiPdf.App.Views;

/// <summary>
/// Lecture et modification du document : barre d'outils, vignettes (sélection, glisser-déposer pour réordonner)
/// et pages. Les images des pages sont dessinées à la demande, seulement pour les pages à l'écran.
/// </summary>
public sealed partial class DocumentView : UserControl
{
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

    // Défilement demandé par le code (aller à la page) : la page visible est déjà connue.
    private bool _scrollingToPage;

    public DocumentView()
    {
        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<DocumentViewModel>();
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
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.Zoom))
            {
                OnZoomChanged();
            }
        };

        Scroller.SizeChanged += (_, e) => ViewModel.ViewportWidth = e.NewSize.Width;
        Scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnScrollerPointerWheelChanged), handledEventsToo: true);
        AddShortcuts();
    }

    public DocumentViewModel ViewModel { get; }

    public SettingsViewModel Settings { get; }

    private DialogService Dialogs { get; }

    /// <summary>Nombre de pages dessinées à l'écran (test de fumée).</summary>
    internal int RenderedPageCount => _realized.Keys.Count(e => e is Border { Child: Image { Source: not null } });

    /// <summary>Raccourcis de Windows (Ctrl+O, Ctrl+S…), actifs même quand le bouton est dans le menu « … ».</summary>
    private void AddShortcuts()
    {
        Add(VirtualKey.O, VirtualKeyModifiers.Control, ViewModel.OpenCommand);
        Add(VirtualKey.S, VirtualKeyModifiers.Control, ViewModel.SaveCommand);
        Add(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, ViewModel.SaveAsCommand);
        Add(VirtualKey.W, VirtualKeyModifiers.Control, ViewModel.CloseDocumentCommand);
        Add(VirtualKey.Z, VirtualKeyModifiers.Control, ViewModel.UndoCommand);
        Add(VirtualKey.Y, VirtualKeyModifiers.Control, ViewModel.RedoCommand);
        Add(VirtualKey.R, VirtualKeyModifiers.Control, ViewModel.RotateRightCommand);
        Add(VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, ViewModel.RotateLeftCommand);
        Add(VirtualKey.I, VirtualKeyModifiers.Control, ViewModel.InsertCommand);
        Add(VirtualKey.E, VirtualKeyModifiers.Control, ViewModel.ExtractCommand);
        Add(VirtualKey.D, VirtualKeyModifiers.Control, ViewModel.EditPropertiesCommand);
        Add((VirtualKey)0xBB, VirtualKeyModifiers.Control, ViewModel.ZoomInCommand); // « = / + » du clavier principal
        Add(VirtualKey.Add, VirtualKeyModifiers.Control, ViewModel.ZoomInCommand);
        Add((VirtualKey)0xBD, VirtualKeyModifiers.Control, ViewModel.ZoomOutCommand); // « - » du clavier principal
        Add(VirtualKey.Subtract, VirtualKeyModifiers.Control, ViewModel.ZoomOutCommand);
        Add(VirtualKey.Number0, VirtualKeyModifiers.Control, ViewModel.ActualSizeCommand);
        Add(VirtualKey.NumberPad0, VirtualKeyModifiers.Control, ViewModel.ActualSizeCommand);

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, ICommand command)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }
            };
            KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>Nouveau contenu : pages redessinées, sélection et position rétablies.</summary>
    private void OnContentChanged(object? sender, DocumentChangedEventArgs e)
    {
        _syncingSelection = true;
        try
        {
            Thumbnails.SelectedItems.Clear();
            foreach (var index in e.Selection)
            {
                Thumbnails.SelectRange(new ItemIndexRange(index, 1));
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        RenderVisiblePages();
        RenderVisibleThumbnails();

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

        _scrollingToPage = true;
        PagesRepeater.UpdateLayout();
        var element = PagesRepeater.GetOrCreateElement(index);
        element.UpdateLayout();
        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
    }

    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            return;
        }

        if (_scrollingToPage)
        {
            _scrollingToPage = false;
            return;
        }

        // Page visible : celle qui occupe le premier quart de l'écran.
        var probe = Scroller.ViewportHeight / 4;
        foreach (var (element, index) in _realized)
        {
            var top = element.TransformToVisual(Scroller).TransformPoint(default).Y;
            if (top <= probe && top + element.ActualHeight + PageSpacing > probe)
            {
                if (ViewModel.CurrentPageNumber != index + 1)
                {
                    ViewModel.CurrentPageNumber = index + 1;
                }

                return;
            }
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
        }
    }

    private void OnPageElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            _realized[element] = args.NewIndex;
            _ = RenderPageAsync(element, args.NewIndex);
        }
    }

    private void OnPageElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            _realized.Remove(element);
            element.Tag = null;
            if (element is Border { Child: Image image })
            {
                image.Source = null;
            }
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
        if (element is not Border { Child: Image image }
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

    // ----- Vignettes -----

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

    // ----- Fichiers déposés depuis l'Explorateur -----

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Drop_Open");
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var pdf = items.OfType<StorageFile>()
                .FirstOrDefault(f => string.Equals(Path.GetExtension(f.Path), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (pdf is not null)
            {
                await ViewModel.OpenFileAsync(pdf.Path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ouverture du fichier déposé impossible");
        }
    }
}
