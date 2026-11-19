using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using QuaiPdf.App.Services;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App;

/// <summary>Point d'entrée de l'application : conteneur de services, fenêtre principale, erreurs non gérées.</summary>
public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private readonly string? _smokeTestDirectory;

    public App()
    {
        if (SmokeTest.TryGetOutputDirectory(out var smokeDirectory))
        {
            _smokeTestDirectory = smokeDirectory;
            SmokeTest.IsolateData(smokeDirectory);
        }

        Services = AppServices.Build();
        _logger = Services.GetRequiredService<ILogger<App>>();

        // Interface en français d'abord : force aussi les textes fournis par WinUI (boutons, menus).
        TrySetLanguage("fr-FR");

        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.LogError(e.Exception, "Erreur non observée dans une tâche");
            e.SetObserved();
        };
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    public MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("Démarrage de Quai PDF {Version}", typeof(App).Assembly.GetName().Version);

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (_smokeTestDirectory is not null)
        {
            _ = SmokeTest.RunAsync(MainWindow, Services, _logger, _smokeTestDirectory);
        }
        else if (FileToOpen() is { } path)
        {
            _ = OpenAtStartupAsync(path);
        }
    }

    /// <summary>Fichier passé en argument (« Ouvrir avec », double-clic, glisser sur l'icône).</summary>
    private static string? FileToOpen() =>
        Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg));

    private async Task OpenAtStartupAsync(string path)
    {
        try
        {
            await Services.GetRequiredService<DocumentViewModel>().OpenFileAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ouverture au démarrage impossible");
        }
    }

    private void TrySetLanguage(string language)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de forcer la langue {Language}", language);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Erreur non gérée");

        if (_smokeTestDirectory is { } testDirectory)
        {
            SmokeTest.Fail(testDirectory, e.Exception);
            return;
        }

        // Une erreur inattendue dans un gestionnaire d'interface ne doit pas fermer l'application
        // et faire perdre les modifications en cours : elle est journalisée et l'exécution continue.
        e.Handled = true;
    }
}
