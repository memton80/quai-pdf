namespace QuaiPdf.Core.Models;

public enum AppTheme
{
    /// <summary>Suit le thème de Windows.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Volets à gauche du document, ouverts par le rail d'icônes.</summary>
public enum SidePanel
{
    /// <summary>Vignettes des pages (sélection, glisser-déposer).</summary>
    Pages,

    /// <summary>Plan du document (signets).</summary>
    Outline,

    /// <summary>Résultats de la recherche dans le texte.</summary>
    Search,

    /// <summary>Propriétés du document et du fichier.</summary>
    Properties,
}

/// <summary>Réglages de l'application (fichier <c>settings.json</c>).</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// Volet latéral ouvert à gauche du document (nom historique : le volet n'avait que les vignettes).
    /// </summary>
    public bool ShowThumbnails { get; set; } = true;

    /// <summary>Dernier volet choisi dans le rail : celui des nouveaux onglets.</summary>
    public SidePanel SidePanel { get; set; } = SidePanel.Pages;

    /// <summary>Au démarrage, demander à GitHub si une version plus récente est publiée (comme Quai).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Ramène les valeurs dans leurs bornes (fichier modifié à la main, ancienne version…).</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.System;
        }

        if (!Enum.IsDefined(SidePanel))
        {
            SidePanel = SidePanel.Pages;
        }
    }
}
