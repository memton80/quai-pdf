using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using QuaiPdf.App.Services;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App;

/// <summary>Point d'entrée de l'application : conteneur de services, fenêtre principale, erreurs non gérées.</summary>
public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private readonly string? _smokeTestDirectory;
    private readonly string? _updateTestDirectory;

    // Passé par l'installateur quand il relance Quai PDF après une mise à jour.
    private const string AfterUpdateSwitch = "--after-update";

    public App()
    {
        if (SmokeTest.TryGetOutputDirectory(out var smokeDirectory))
        {
            _smokeTestDirectory = smokeDirectory;
            SmokeTest.IsolateData(smokeDirectory);
        }

        string? updateTestInstaller = null;
        if (SmokeTest.TryGetUpdateTest(out var updateDirectory, out var installer))
        {
            _updateTestDirectory = updateDirectory;
            updateTestInstaller = installer;
            SmokeTest.IsolateData(updateDirectory);
        }

        Services = AppServices.Build(smokeTest: _smokeTestDirectory is not null, updateTestInstaller);
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
        var afterUpdate = Environment.GetCommandLineArgs().Contains(AfterUpdateSwitch, StringComparer.OrdinalIgnoreCase);
        if (afterUpdate)
        {
            // Relancé par l'installateur au bout du bouton « Mettre à jour » (installer/QuaiPdf.iss).
            _logger.LogInformation("Démarrage juste après une mise à jour (relancé par l'installateur)");
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (_smokeTestDirectory is not null)
        {
            _ = SmokeTest.RunAsync(MainWindow, Services, _logger, _smokeTestDirectory);
            return;
        }

        if (_updateTestDirectory is not null)
        {
            _ = SmokeTest.RunUpdateAsync(MainWindow, Services, _logger, _updateTestDirectory);
            return;
        }

        if (((afterUpdate ? DocumentToReopen() : null) ?? FileToOpen()) is { } path)
        {
            _ = OpenAtStartupAsync(path);
        }

        _ = CheckForUpdatesAsync();
    }

    /// <summary>Vérification des mises à jour, un peu après le démarrage pour ne pas le ralentir.</summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await Services.GetRequiredService<UpdateBannerViewModel>().CheckAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vérification des mises à jour interrompue");
        }
    }

    /// <summary>Document ouvert au moment de la mise à jour (noté par UpdateInstaller), rouvert une seule fois.</summary>
    private string? DocumentToReopen()
    {
        var file = Services.GetRequiredService<AppPaths>().ReopenAfterUpdateFile;
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var path = File.ReadAllText(file).Trim();
            File.Delete(file);
            if (!File.Exists(path))
            {
                return null;
            }

            _logger.LogInformation("Document rouvert après la mise à jour ({File})", Path.GetFileName(path));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Document à rouvrir après la mise à jour illisible ({Error})", ex.GetType().Name);
            return null;
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

        if ((_smokeTestDirectory ?? _updateTestDirectory) is { } testDirectory)
        {
            SmokeTest.Fail(testDirectory, e.Exception);
            return;
        }

        // Une erreur inattendue dans un gestionnaire d'interface ne doit pas fermer l'application
        // et faire perdre les modifications en cours : elle est journalisée et l'exécution continue.
        e.Handled = true;
    }
}
