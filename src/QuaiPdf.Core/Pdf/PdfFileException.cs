namespace QuaiPdf.Core.Pdf;

public enum PdfProblem
{
    /// <summary>Le fichier n'est pas un PDF lisible (abîmé, tronqué, autre format).</summary>
    Unreadable,

    /// <summary>Le fichier est protégé par un mot de passe.</summary>
    Protected,
}

/// <summary>Fichier PDF impossible à lire ou à modifier ; <see cref="Problem"/> donne le message à afficher.</summary>
public sealed class PdfFileException : Exception
{
    public PdfFileException(PdfProblem problem, string message, Exception? inner = null)
        : base(message, inner)
    {
        Problem = problem;
    }

    public PdfProblem Problem { get; }
}

/// <summary>Le moteur d'affichage demande un mot de passe (absent ou incorrect).</summary>
public sealed class PdfPasswordException : Exception
{
    public PdfPasswordException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
