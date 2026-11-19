namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Moteur d'affichage des pages (implémenté par l'application avec le moteur PDF de Windows, <c>Windows.Data.Pdf</c>).
/// Seul juge de ce qui s'affiche : un fichier que PDFsharp ne sait pas réécrire peut tout de même être lu.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>Prépare l'affichage d'un contenu PDF.</summary>
    /// <exception cref="PdfPasswordException">Mot de passe absent ou incorrect.</exception>
    /// <exception cref="PdfFileException">Contenu illisible.</exception>
    Task<IRenderedPdf> LoadAsync(byte[] content, string? password, CancellationToken cancellationToken = default);
}

/// <summary>Un contenu PDF prêt à être affiché ; les images des pages sont produites par l'application.</summary>
public interface IRenderedPdf : IDisposable
{
    /// <summary>Taille de chaque page, rotation comprise.</summary>
    IReadOnlyList<PdfPageSize> Pages { get; }
}
