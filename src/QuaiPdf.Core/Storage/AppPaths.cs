namespace QuaiPdf.Core.Storage;

/// <summary>
/// Emplacements des fichiers de l'application (par défaut <c>%LOCALAPPDATA%\QuaiPdf</c>).
/// La variable d'environnement <c>QUAI_PDF_DATA_DIR</c> permet de rediriger le tout (tests, CI).
/// </summary>
public sealed class AppPaths
{
    public const string DataDirectoryVariable = "QUAI_PDF_DATA_DIR";

    public AppPaths(string? root = null)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        Root = !string.IsNullOrWhiteSpace(root)
            ? root
            : !string.IsNullOrWhiteSpace(fromEnvironment)
                ? fromEnvironment
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Installateur de la mise à jour en cours (effacé au démarrage suivant).</summary>
    public string UpdatesDirectory => Path.Combine(Root, "updates");

    /// <summary>Document ouvert au moment d'une mise à jour : rouvert par la nouvelle version, puis effacé.</summary>
    public string ReopenAfterUpdateFile => Path.Combine(UpdatesDirectory, "reopen.txt");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
