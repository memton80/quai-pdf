using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Services;

/// <summary>Réponse à « Enregistrer les modifications ? ».</summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Boîtes de dialogue de l'application, vues depuis les view-models (implémentées par l'interface WinUI).</summary>
public interface IDialogService
{
    /// <summary>Demande une confirmation ; <paramref name="isDestructive"/> signale une action irréversible.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false);

    Task ShowErrorAsync(string title, string message);

    /// <summary>Mot de passe d'un PDF protégé ; <c>null</c> si l'utilisateur annule. Jamais enregistré.</summary>
    Task<string?> PromptPasswordAsync(string fileName, bool isRetry);

    /// <summary>Modifications non enregistrées : enregistrer, abandonner ou annuler l'action en cours.</summary>
    Task<SaveChoice> AskSaveChangesAsync(string fileName);

    /// <summary>Fiche des propriétés du document ; retourne vrai si l'utilisateur a validé.</summary>
    Task<bool> EditMetadataAsync(MetadataEditorViewModel editor);
}
