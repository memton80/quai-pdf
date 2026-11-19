namespace QuaiPdf.Core.Pdf;

/// <summary>Ce que Quai PDF peut faire d'un fichier qui s'affiche correctement.</summary>
public enum PdfEditability
{
    /// <summary>Pages et propriétés modifiables.</summary>
    Editable,

    /// <summary>Chiffré ou protégé contre la modification (mot de passe du propriétaire) : lecture seule.</summary>
    Protected,

    /// <summary>Structure que PDFsharp ne sait pas réécrire sans risque : lecture seule.</summary>
    Unsupported,
}
