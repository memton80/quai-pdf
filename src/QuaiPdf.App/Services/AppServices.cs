using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuaiPdf.Core.Localization;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.ViewModels;
using Serilog;

namespace QuaiPdf.App.Services;

/// <summary>Composition des services de l'application (DI + journalisation fichier).</summary>
internal static class AppServices
{
    public static ServiceProvider Build()
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

        return services.BuildServiceProvider();
    }
}
