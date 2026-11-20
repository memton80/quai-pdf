namespace QuaiPdf.Core.Updates;

/// <summary>Côté application : ce que QuaiPdf.Core ne fait pas lui-même (registre, processus, fenêtre, navigateur).</summary>
public interface IUpdateInstaller
{
    /// <summary>Quai PDF tourne depuis son installation : l'installateur peut le mettre à jour (pas une copie portable).</summary>
    bool CanInstall { get; }

    /// <summary>
    /// Propose d'enregistrer le document modifié, lance l'installateur vérifié en mode silencieux puis quitte
    /// Quai PDF ; l'installateur relance la nouvelle version une fois installée, qui rouvre le document.
    /// </summary>
    /// <returns><c>false</c> si l'utilisateur annule (document modifié gardé ouvert) : rien n'est lancé.</returns>
    Task<bool> InstallAndRestartAsync(string installerPath);

    /// <summary>Ouvre la page de la release dans le navigateur (copie portable, installateur absent de la release).</summary>
    void OpenReleasePage(Uri page);
}
