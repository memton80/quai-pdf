using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.Updates;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App.Services;

/// <summary>
/// Installation d'une mise à jour téléchargée (repris de Quai) : l'installateur Inno Setup de la release est lancé en
/// mode silencieux (petite fenêtre de progression, aucune question), Quai PDF se ferme, et l'installateur le relance
/// une fois à jour (<c>/RELAUNCH=1</c>, voir <c>installer/QuaiPdf.iss</c>). Installation par utilisateur : aucune
/// élévation. Un document modifié est d'abord proposé à l'enregistrement ; le document ouvert est rouvert ensuite.
/// </summary>
internal sealed class UpdateInstaller : IUpdateInstaller
{
    // Entrée de désinstallation écrite par Inno Setup (AppId de installer/QuaiPdf.iss, à ne jamais changer).
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{881A2EB2-5817-4B9E-AA96-BEBBED0A8E2E}_is1";

    private readonly DocumentViewModel _document;
    private readonly AppPaths _paths;
    private readonly ILogger _logger;
    private bool? _canInstall;

    public UpdateInstaller(DocumentViewModel document, AppPaths paths, ILogger<UpdateInstaller> logger)
    {
        _document = document;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Quai PDF tourne depuis le dossier où l'installateur l'a mis (sinon : copie portable ou développement).</summary>
    public bool CanInstall => _canInstall ??= IsRunningFromInstallation();

    public async Task<bool> InstallAndRestartAsync(string installerPath)
    {
        // Modifications en cours : enregistrer, abandonner, ou annuler (la mise à jour attend, déjà téléchargée).
        if (!await _document.ConfirmCloseAsync())
        {
            return false;
        }

        RememberDocument();

        var start = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
        };

        // /SILENT : seulement la fenêtre de progression ; /CLOSEAPPLICATIONS : ferme Quai PDF s'il n'a pas encore fini
        // de quitter ; /RELAUNCH=1 : relance Quai PDF une fois installé ; journal à côté de ceux de Quai PDF.
        foreach (var argument in new[]
        {
            "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/RELAUNCH=1",
            $"/LOG={Path.Combine(_paths.LogsDirectory, "update-install.log")}",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using (Process.Start(start) ?? throw new InvalidOperationException("L'installateur ne s'est pas lancé."))
        {
        }

        _logger.LogInformation("Installateur de la mise à jour lancé ({File}) ; fermeture de Quai PDF", Path.GetFileName(installerPath));
        App.Current.MainWindow?.CloseForUpdate();
        return true;
    }

    public void OpenReleasePage(Uri page)
    {
        try
        {
            // Navigateur par défaut ; l'adresse est construite par l'application (page de release GitHub).
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ouverture de la page de mise à jour impossible ({Error})", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Document à rouvrir après la mise à jour (sur le disque, tel qu'enregistré ou abandonné juste avant).
    /// Lu et effacé par la nouvelle version (<c>--after-update</c>).
    /// </summary>
    private void RememberDocument()
    {
        if (_document.FilePath is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_paths.UpdatesDirectory);
            File.WriteAllText(_paths.ReopenAfterUpdateFile, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La mise à jour passe avant : le document se rouvrira à la main.
            _logger.LogWarning("Document à rouvrir après la mise à jour non noté ({Error})", ex.GetType().Name);
        }
    }

    private bool IsRunningFromInstallation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is not string location || string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            var installed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
            var running = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var same = string.Equals(installed, running, StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Mise à jour en place {State}", same ? "possible" : "impossible (copie portable)");
            return same;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            _logger.LogInformation("Installation introuvable dans le registre ({Error})", ex.GetType().Name);
            return false;
        }
    }
}
