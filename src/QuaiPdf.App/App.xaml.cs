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

        // Instance unique : les PDF ouverts depuis l'Explorateur pendant que Quai PDF tourne arrivent ici.
        var window = MainWindow;
        SingleInstance.Listen(files => window.DispatcherQueue.TryEnqueue(() => _ = OpenReceivedAsync(window, files)));

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

        List<string> paths = afterUpdate ? DocumentsToReopen() : [];
        if (paths.Count == 0)
        {
            paths = FilesToOpen();
        }

        if (paths.Count > 0)
        {
            _ = OpenAtStartupAsync(paths);
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

    /// <summary>Documents ouverts au moment de la mise à jour (notés par UpdateInstaller), rouverts une seule fois.</summary>
    private List<string> DocumentsToReopen()
    {
        var file = Services.GetRequiredService<AppPaths>().ReopenAfterUpdateFile;
        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            var paths = File.ReadAllLines(file).Select(line => line.Trim()).Where(File.Exists).ToList();
            File.Delete(file);
            foreach (var path in paths)
            {
                _logger.LogInformation("Document rouvert après la mise à jour ({File})", Path.GetFileName(path));
            }

            return paths;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Documents à rouvrir après la mise à jour illisibles ({Error})", ex.GetType().Name);
            return [];
        }
    }

    /// <summary>Fichiers passés en argument (« Ouvrir avec », double-clic, glisser sur l'icône), chacun dans son onglet.</summary>
    private static List<string> FilesToOpen() =>
        Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg))
            .ToList();

    /// <summary>Lancement transmis par un second Quai PDF : la fenêtre passe devant et ouvre les fichiers en onglets.</summary>
    private async Task OpenReceivedAsync(MainWindow window, IReadOnlyList<string> paths)
    {
        try
        {
            _logger.LogInformation("Lancement transmis par une autre instance : {Count} fichier(s)", paths.Count);
            window.BringToFront();
            await Services.GetRequiredService<WorkspaceViewModel>().OpenFilesAsync(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ouverture des fichiers transmis impossible");
        }
    }

    private async Task OpenAtStartupAsync(IReadOnlyList<string> paths)
    {
        try
        {
            await Services.GetRequiredService<WorkspaceViewModel>().OpenFilesAsync(paths);
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
