using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QuaiPdf.Core.Feedback;
using QuaiPdf.Core.Localization;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App.Services;

/// <summary>
/// Bouton « Idée ou bug » (repris de Quai) : formulaire dans Quai PDF, puis page GitHub d'une nouvelle issue déjà
/// remplie dans le navigateur. Quai PDF n'envoie rien lui-même (pas de serveur, pas de jeton) : l'utilisateur relit et
/// publie sur GitHub.
/// </summary>
internal sealed class FeedbackSender(IStrings strings, DialogService dialogs, IClipboard clipboard, AppPaths paths, ILogger<FeedbackSender> logger)
{
    public async Task RunAsync()
    {
        var feedback = new FeedbackViewModel(strings, FeedbackIssue.DescribeSystem());
        if (!await dialogs.ShowFeedbackAsync(feedback, paths.LogsDirectory))
        {
            return;
        }

        var link = feedback.BuildLink();
        if (!OpenInBrowser(link.Address))
        {
            CopyToClipboard(link.FullText);
            await dialogs.ShowErrorAsync(
                strings.Get("Feedback_BrowserFailedTitle"),
                strings.Format("Feedback_BrowserFailedMessage", FeedbackIssue.NewIssuePage.AbsoluteUri));
            return;
        }

        if (link.Shortened)
        {
            // Adresse limitée par Windows : la fin du texte manque sur GitHub, le texte complet attend dans le presse-papiers.
            CopyToClipboard(link.FullText);
            await dialogs.ShowErrorAsync(strings.Get("Feedback_ShortenedTitle"), strings.Get("Feedback_ShortenedMessage"));
        }
    }

    private bool OpenInBrowser(Uri address)
    {
        try
        {
            // Navigateur par défaut ; l'adresse est construite par l'application (page « nouvelle issue » du dépôt).
            Process.Start(new ProcessStartInfo(address.OriginalString) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Ouverture du navigateur impossible ({Error})", ex.GetType().Name);
            return false;
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            // Presse-papiers verrouillé par une autre application.
            logger.LogWarning("Copie dans le presse-papiers impossible ({Error})", ex.GetType().Name);
        }
    }
}
