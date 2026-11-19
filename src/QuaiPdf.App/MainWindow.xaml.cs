using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using QuaiPdf.App.Services;
using QuaiPdf.App.Views;
using QuaiPdf.Core.ViewModels;
using Windows.Graphics;

namespace QuaiPdf.App;

/// <summary>Fenêtre principale : barre de titre et document.</summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;

    // Fermeture : question en cours, puis fermeture accordée (modifications enregistrées ou abandonnées).
    private bool _closeRequested;
    private bool _closeAllowed;

    public MainWindow()
    {
        var services = App.Current.Services;
        Document = services.GetRequiredService<DocumentViewModel>();
        _logger = services.GetRequiredService<ILogger<MainWindow>>();
        InitializeComponent();

        Title = Document.WindowTitle;
        Document.PropertyChanged += OnDocumentPropertyChanged;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "QuaiPdf.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // Les boîtes de dialogue et sélecteurs de fichiers ont besoin de la fenêtre.
        services.GetRequiredService<DialogService>().Root = RootGrid;
        services.GetRequiredService<FileDialogs>().WindowId = AppWindow.Id;
        services.GetRequiredService<ThemeService>().Attach(this, RootGrid);

        AppWindow.Closing += OnClosing;
        ResizeForDpi(1240, 860);
    }

    public DocumentViewModel Document { get; }

    /// <summary>Contenu racine de la fenêtre (test de fumée).</summary>
    public FrameworkElement Root => RootGrid;

    internal DocumentView View => DocumentHost;

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.WindowTitle))
        {
            Title = Document.WindowTitle;
        }
    }

    /// <summary>Fermeture de la fenêtre (bouton, Alt+F4) : propose d'enregistrer les modifications en cours.</summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeAllowed || !Document.IsModified)
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
            if (!await Document.ConfirmCloseAsync())
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
