namespace QuaiPdf.Core.Services;

/// <summary>Sélecteurs de fichiers de Windows (implémentés par l'application).</summary>
public interface IFileDialogs
{
    /// <summary>Un PDF à ouvrir ; <c>null</c> si l'utilisateur annule.</summary>
    Task<string?> PickPdfToOpenAsync();

    /// <summary>Un ou plusieurs PDF dont insérer les pages ; liste vide si l'utilisateur annule.</summary>
    Task<IReadOnlyList<string>> PickPdfsToInsertAsync();

    /// <summary>Emplacement d'enregistrement d'un PDF ; <c>null</c> si l'utilisateur annule.</summary>
    Task<string?> PickSaveLocationAsync(string suggestedFileName, string? folder);
}
