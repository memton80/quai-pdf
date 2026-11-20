using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuaiPdf.Core;
using QuaiPdf.Core.Localization;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.Updates;
using QuaiPdf.Core.ViewModels;
using Serilog;

namespace QuaiPdf.App.Services;

/// <summary>Composition des services de l'application (DI + journalisation fichier).</summary>
internal static class AppServices
{
    /// <param name="smokeTest">Test de fumée : la release « plus récente » est fictive, aucune requête réseau.</param>
    /// <param name="updateTestInstaller">Test de la mise à jour (CI) : cet installateur local est présenté comme la dernière release.</param>
    public static ServiceProvider Build(bool smokeTest = false, string? updateTestInstaller = null)
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        // Journal local uniquement (pas de télémétrie). Ni contenu de document ni mot de passe.
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "quaipdf-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: true));

        // Réglages
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        // Interface
        services.AddSingleton<IStrings, ReswStrings>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<FileDialogs>();
        services.AddSingleton<IFileDialogs>(provider => provider.GetRequiredService<FileDialogs>());
        services.AddSingleton<IClipboard, ClipboardService>();

        // PDF : affichage par Windows, modifications par PDFsharp (dans QuaiPdf.Core)
        services.AddSingleton<IPdfRenderer, WindowsPdfRenderer>();

        // View-models
        services.AddSingleton<DocumentViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Mises à jour (comme Quai) : une requête à l'API publique de GitHub, puis, sur demande, le téléchargement de
        // l'installateur (les tests de fumée n'accèdent pas au réseau).
        var installerName = UpdateChecker.InstallerNameFor(RuntimeInformation.ProcessArchitecture);
        if (updateTestInstaller is not null)
        {
            var (source, downloader) = SmokeUpdate.Create(updateTestInstaller, installerName);
            services.AddSingleton(source);
            services.AddSingleton(downloader);
        }
        else
        {
            services.AddSingleton<IReleaseSource>(smokeTest ? new SmokeReleaseSource() : new GitHubReleaseSource());
            services.AddSingleton(_ => new UpdateDownloader());
        }

        services.AddSingleton(provider => new UpdateChecker(provider.GetRequiredService<IReleaseSource>(), AppInfo.Version, installerName));
        services.AddSingleton<IUpdateInstaller, UpdateInstaller>();
        services.AddSingleton<UpdateBannerViewModel>();

        return services.BuildServiceProvider();
    }
}
