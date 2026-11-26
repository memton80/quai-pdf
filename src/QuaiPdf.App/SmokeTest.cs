using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QuaiPdf.App.Services;
using QuaiPdf.App.Views;
using QuaiPdf.Core.Feedback;
using QuaiPdf.Core.Localization;
using QuaiPdf.Core.Models;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.Updates;
using QuaiPdf.Core.ViewModels;
using Windows.Graphics;

namespace QuaiPdf.App;

/// <summary>
/// Test de fumée lancé par le CI : <c>QuaiPdf.exe --smoke-test &lt;dossier&gt;</c>.
/// Ouvre la fenêtre, un document d'exemple, ses volets (plan, recherche, propriétés), le modifie (rotation,
/// suppression, annulation, ordre), l'enregistre, relit le résultat, ouvre les réglages et un document protégé dans un
/// second onglet, ferme les onglets, enregistre des captures, puis quitte avec le code 0
/// (fichier <c>smoke-ok.txt</c>) ou 1 (fichier <c>smoke-error.txt</c>). Données dans un dossier jetable.
/// </summary>
internal static class SmokeTest
{
    private const string Switch = "--smoke-test";
    private const string UpdateSwitch = "--update-test";

    /// <summary>Version « plus récente » des tests : jamais publiée pour de vrai.</summary>
    public const string ReleaseTag = "v99.0.0";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    public static bool TryGetOutputDirectory(out string directory)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            directory = string.Empty;
            return false;
        }

        directory = index + 1 < args.Length
            ? Path.GetFullPath(args[index + 1])
            : Path.Combine(Path.GetTempPath(), "quaipdf-smoke");
        return true;
    }

    /// <summary>
    /// Mise à jour en place (CI, repris de Quai) : <c>QuaiPdf.exe --update-test &lt;dossier&gt; &lt;installateur&gt;</c>.
    /// Quai PDF, installé, se voit proposer cet installateur comme nouvelle version, le télécharge, le vérifie, le lance
    /// en mode silencieux et se ferme ; le workflow vérifie ensuite que l'installateur a réussi, a relancé Quai PDF, et
    /// que le document ouvert avant la mise à jour a été rouvert.
    /// </summary>
    public static bool TryGetUpdateTest(out string directory, out string installer)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, UpdateSwitch, StringComparison.OrdinalIgnoreCase));
        var found = index >= 0 && index + 2 < args.Length;
        directory = found ? Path.GetFullPath(args[index + 1]) : string.Empty;
        installer = found ? Path.GetFullPath(args[index + 2]) : string.Empty;
        return found;
    }

    public static void IsolateData(string directory) =>
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Path.Combine(directory, "data"));

    public static async Task RunAsync(MainWindow window, IServiceProvider services, ILogger logger, string directory)
    {
        var report = new List<string>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Directory.CreateDirectory(directory);
            FitToScreen(window);
            await Task.Delay(1500);
            await CaptureAsync(hwnd, directory, "1-accueil.png", report);

            var workspace = services.GetRequiredService<WorkspaceViewModel>();
            var settings = services.GetRequiredService<SettingsViewModel>();

            // Ouverture : trois pages de tailles différentes, toutes modifiables, dans l'onglet d'accueil.
            var sample = Path.Combine(directory, "exemple.pdf");
            File.WriteAllBytes(sample, SampleDocument.Create());
            Check(await WithTimeout(workspace.OpenFileAsync(sample), "ouverture"), "ouverture du document d'exemple");
            var document = workspace.Selected;
            Check(workspace.Documents.Count == 1, $"le premier document s'ouvre dans l'onglet d'accueil ({workspace.Documents.Count} onglets)");
            Check(ReferenceEquals(window.SelectedTab, document), "onglet du document sélectionné dans la barre de titre");
            Check(document.PageCount == 3, $"3 pages attendues, {document.PageCount} affichées");
            Check(document.Editability == PdfEditability.Editable, $"document modifiable attendu ({document.Editability})");

            // Les tailles données par Windows (affichage) sont celles que calcule PDFsharp (pixels à 96 ppp).
            var expected = PdfOperations.PageSizes(File.ReadAllBytes(sample));
            for (var i = 0; i < expected.Count; i++)
            {
                var shown = document.Pages[i].Size;
                Check(
                    Math.Abs(shown.Width - expected[i].Width) < 1.5 && Math.Abs(shown.Height - expected[i].Height) < 1.5,
                    $"page {i + 1} : taille Windows {Describe(shown)} au lieu de {Describe(expected[i])}");
            }

            Check(window.View.ShownPageNumber == 1, $"numéro de page affiché : {window.View.ShownPageNumber} au lieu de 1");
            Check(!window.View.ReadOnlyBannerShown, "bandeau « Lecture seule » sur un document modifiable");
            report.Add("Tailles des pages (Windows) : " + string.Join(" ; ", document.Pages.Select(p => Describe(p.Size))));

            // Images des pages : dessinées par le moteur PDF de Windows.
            var rendered = document.Rendered as WindowsRenderedPdf ?? throw new InvalidOperationException("Moteur d'affichage inattendu.");
            var bitmap = await rendered.RenderAsync(0, 400);
            Check(bitmap is { PixelWidth: 400 }, $"rendu direct de la page 1 : {bitmap?.PixelWidth} px de large");
            await WaitForAsync(() => window.View.RenderedPageCount > 0, "pages dessinées à l'écran");
            report.Add($"Pages dessinées à l'écran : {window.View.RenderedPageCount}");
            await Task.Delay(800);
            await CaptureAsync(hwnd, directory, "2-document.png", report);

            // Première ouverture : la zone de lecture n'avait pas encore de taille, le zoom s'ajuste quand elle en a une.
            await WaitForAsync(() => document.ViewportWidth > 0, "largeur de la zone de lecture");
            var fit = Math.Min((document.ViewportWidth - 48) / document.Pages.Max(p => p.Size.Width), 1.5);
            await WaitForAsync(() => Math.Abs(document.Zoom - fit) < 0.01, $"zoom « ajuster à la largeur » à l'ouverture ({fit:P0}, affiché : {document.ZoomText})");
            report.Add($"Zoom à l'ouverture : {document.ZoomText}");

            await TextAsync(window, document, services.GetRequiredService<DialogService>(), hwnd, directory, report);
            await FollowVisiblePageAsync(window, document, hwnd, directory, report);
            await PanelsAsync(window, document, hwnd, directory, report);
            await TextBoxesAsync(window, document, hwnd, directory, report);

            // Modifications : rotation, suppression annulée, déplacement.
            document.SetSelection([0]);
            await document.RotateRightCommand.ExecuteAsync(null);
            Check(PdfOperations.PageRotations(document.Content!)[0] == 90, "rotation de la page 1");
            Check(document.Pages[0].Size.Width > document.Pages[0].Size.Height, "page 1 tournée affichée en paysage");

            document.SetSelection([1]);
            await document.DeleteCommand.ExecuteAsync(null);
            Check(document.PageCount == 2, "suppression de la page 2");
            await document.UndoCommand.ExecuteAsync(null);
            Check(document.PageCount == 3, "annulation de la suppression");

            document.SetSelection([2]);
            await document.MoveUpCommand.ExecuteAsync(null);
            Check(document.IsModified, "document marqué modifié");
            Check(window.Title.StartsWith('*'), $"titre de fenêtre « {window.Title} »");

            document.ZoomInCommand.Execute(null);
            document.FitWidthCommand.Execute(null);
            report.Add($"Zoom ajusté à la largeur : {document.ZoomText}");
            await Task.Delay(1000);
            await CaptureAsync(hwnd, directory, "3-modifie.png", report);

            // Enregistrement, puis relecture du fichier écrit.
            var saved = Path.Combine(directory, "exemple-modifie.pdf");
            Check(await WithTimeout(document.SaveToAsync(saved), "enregistrement"), "enregistrement");
            Check(!document.IsModified, "document enregistré");
            var content = File.ReadAllBytes(saved);
            Check(PdfOperations.PageRotations(content).SequenceEqual([90, 0, 0]), "rotations du fichier enregistré");
            var widths = PdfOperations.PageSizes(content).Select(s => Math.Round(s.Width / PdfPageSize.PixelsPerPoint)).ToList();
            Check(widths.SequenceEqual([842d, 612d, 842d]), $"ordre des pages du fichier enregistré ({string.Join(", ", widths)})");
            report.Add($"Fichier enregistré : {content.Length} octets, pages {string.Join(", ", widths)} pt de large");

            // Réglages et thème sombre.
            var dialogs = services.GetRequiredService<DialogService>();
            var settingsDialog = dialogs.ShowSettingsAsync(settings);
            await Task.Delay(1200);
            await CaptureAsync(hwnd, directory, "4-reglages.png", report);
            dialogs.Current?.Hide();
            await WithTimeout(settingsDialog, "fermeture des réglages");

            await FeedbackAsync(services, hwnd, directory, report);

            settings.ThemeIndex = 2;
            await Task.Delay(1000);
            await CaptureAsync(hwnd, directory, "5-sombre.png", report);
            settings.ThemeIndex = 0;

            await UpdateBannerAsync(window, services, hwnd, directory, report);

            // Document protégé contre la modification : dans un second onglet, affiché en lecture seule.
            var restricted = Path.Combine(directory, "protege.pdf");
            File.WriteAllBytes(restricted, SampleDocument.Create(ownerPassword: "proprietaire"));
            Check(await WithTimeout(workspace.OpenFileAsync(restricted), "ouverture du document protégé"), "ouverture du document protégé");
            var locked = workspace.Selected;
            Check(workspace.Documents.Count == 2 && !ReferenceEquals(locked, document), $"document protégé dans un nouvel onglet ({workspace.Documents.Count} onglets)");
            await WaitForAsync(() => ReferenceEquals(window.SelectedTab, locked), "onglet du document protégé sélectionné");
            Check(locked.Editability == PdfEditability.Protected, $"document protégé en lecture seule ({locked.Editability})");
            Check(!locked.RotateRightCommand.CanExecute(null), "rotation désactivée en lecture seule");
            Check(window.View.ReadOnlyBannerShown, "bandeau « Lecture seule » affiché");
            Check(window.View.ShownPageNumber == 1, $"numéro de page affiché : {window.View.ShownPageNumber} au lieu de 1");
            Check(document.HasDocument && document.PageCount == 3, "le premier onglet garde son document");
            await Task.Delay(1000);
            await CaptureAsync(hwnd, directory, "6-lecture-seule.png", report);

            // Un fichier déjà ouvert : son onglet est sélectionné, rien n'est rouvert.
            Check(await WithTimeout(workspace.OpenFileAsync(saved), "retour au premier onglet"), "ouverture d'un fichier déjà ouvert");
            Check(ReferenceEquals(workspace.Selected, document) && workspace.Documents.Count == 2, "fichier déjà ouvert : son onglet est sélectionné");
            await WaitForAsync(() => ReferenceEquals(window.SelectedTab, document), "onglet du premier document sélectionné");
            Check(window.View.ShownPageNumber >= 1, "vue du premier onglet affichée");

            // Fermeture des onglets : le dernier revient à l'accueil.
            Check(await WithTimeout(workspace.CloseAsync(locked), "fermeture de l'onglet protégé"), "fermeture de l'onglet protégé");
            Check(workspace.Documents.Count == 1 && ReferenceEquals(workspace.Selected, document), "un onglet restant après fermeture");
            await workspace.CloseSelectedCommand.ExecuteAsync(null);
            Check(workspace.Documents.Count == 1 && !workspace.Selected.HasDocument, "le dernier onglet fermé revient à l'accueil");
            Check(window.Title == "Quai PDF", $"titre de fenêtre à l'accueil : « {window.Title} »");
            report.Add("Onglets : ouverture dans un nouvel onglet, fichier déjà ouvert retrouvé, fermeture : OK");

            await SingleInstanceAsync(workspace, directory, report);

            report.Add("Test de fumée : OK");
            File.WriteAllLines(Path.Combine(directory, "smoke-report.txt"), report);
            File.WriteAllText(Path.Combine(directory, "smoke-ok.txt"), "ok");
            logger.LogInformation("Test de fumée réussi");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test de fumée en échec");
            report.Add("ÉCHEC : " + ex.Message);
            TryWrite(Path.Combine(directory, "smoke-report.txt"), string.Join(Environment.NewLine, report));
            try
            {
                await CaptureAsync(hwnd, directory, "echec.png", report);
            }
            catch (Exception)
            {
                // La capture d'échec est un plus : son absence ne change rien au diagnostic.
            }

            Fail(directory, ex);
        }
    }

    /// <summary>
    /// Mise à jour en place : document ouvert, bandeau, téléchargement vérifié, installation silencieuse, fermeture.
    /// La suite (installateur, relance, document rouvert) est vérifiée par le workflow.
    /// </summary>
    public static async Task RunUpdateAsync(MainWindow window, IServiceProvider services, ILogger logger, string directory)
    {
        var report = new List<string>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(1500);

            // Document ouvert pendant la mise à jour : la nouvelle version doit le rouvrir.
            var sample = Path.Combine(directory, "avant-mise-a-jour.pdf");
            File.WriteAllBytes(sample, SampleDocument.Create());
            var workspace = services.GetRequiredService<WorkspaceViewModel>();
            Check(await WithTimeout(workspace.OpenFileAsync(sample), "ouverture"), "ouverture du document avant la mise à jour");

            var banner = services.GetRequiredService<UpdateBannerViewModel>();
            await banner.CheckAsync();
            Check(banner.IsOpen, "le bandeau ne propose pas la nouvelle version");
            Check(banner.CanInstallInPlace, "Quai PDF, lancé depuis son installation, ne propose pas la mise à jour en place (installation non reconnue ?)");
            await Task.Delay(500);
            await CaptureAsync(hwnd, directory, "update-available.png", report);

            // Un clic : téléchargement (capturé en cours), vérification, installation silencieuse, fermeture.
            var update = banner.ActAsync();
            await WaitForAsync(() => !banner.IsDownloading || banner.Progress >= 25, "téléchargement de la mise à jour");
            if (banner.IsDownloading)
            {
                await CaptureAsync(hwnd, directory, "update-downloading.png", report);
            }

            logger.LogInformation("Test de la mise à jour : installation lancée");
            await update;
            Check(!banner.HasFailed, $"mise à jour en échec : {banner.Message}");

            // Normalement, Quai PDF est déjà en train de se fermer pour laisser la place à l'installateur.
            File.WriteAllLines(Path.Combine(directory, "smoke-report.txt"), report);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test de la mise à jour en échec");
            TryWrite(Path.Combine(directory, "smoke-report.txt"), string.Join(Environment.NewLine, report));
            Fail(directory, ex);
        }
    }

    /// <summary>Bandeau de mise à jour : une release plus récente (fictive, sans réseau) l'affiche.</summary>
    /// <summary>Formulaire « idée ou bug » (les deux variantes) et adresse GitHub construite ; aucun navigateur n'est ouvert.</summary>
    private static async Task FeedbackAsync(IServiceProvider services, IntPtr hwnd, string directory, List<string> report)
    {
        var dialogs = services.GetRequiredService<DialogService>();
        var strings = services.GetRequiredService<IStrings>();
        var logs = services.GetRequiredService<AppPaths>().LogsDirectory;
        var system = FeedbackIssue.DescribeSystem();

        var idea = new FeedbackViewModel(strings, system)
        {
            Title = "Fusionner plusieurs PDF",
            Description = "Réunir les factures du mois en un seul fichier.",
            AreaIndex = 1,
        };
        var bug = new FeedbackViewModel(strings, system, FeedbackKind.Bug)
        {
            Title = "La page 3 s'affiche à l'envers",
            Description = "Après une rotation, l'aperçu ne suit pas.",
            Steps = "1. Ouvrir un PDF\r2. Pivoter la page 3",
        };

        foreach (var (feedback, name) in new[] { (idea, "4b-idee.png"), (bug, "4c-bug.png") })
        {
            var shown = dialogs.ShowFeedbackAsync(feedback, logs);
            await WaitForAsync(() => dialogs.Current is FeedbackDialog, $"formulaire « {name} » ouvert");
            await Task.Delay(1000);
            await CaptureAsync(hwnd, directory, name, report);
            dialogs.Current?.Hide();
            Check(!await WithTimeout(shown, $"fermeture du formulaire « {name} »"), "formulaire fermé sans continuer vers GitHub");
        }

        var address = bug.BuildLink().Address.OriginalString;
        Check(
            address.StartsWith("https://github.com/memton80/quai-pdf/issues/new?template=bug.yml&title=", StringComparison.Ordinal)
                && address.Contains("&etapes=1.%20Ouvrir%20un%20PDF%0A2.", StringComparison.Ordinal),
            $"adresse GitHub inattendue pour un bug : {address}");
        Check(
            idea.BuildLink().Address.OriginalString.Contains("&domaine=Pages%20", StringComparison.Ordinal),
            "partie concernée absente de l'adresse d'une idée");
        report.Add("Formulaire « idée ou bug » : OK");
    }

    private static async Task UpdateBannerAsync(MainWindow window, IServiceProvider services, IntPtr hwnd, string directory, List<string> report)
    {
        var banner = services.GetRequiredService<UpdateBannerViewModel>();
        await banner.CheckAsync();
        Check(
            banner.IsOpen && banner.ReleasePage?.AbsoluteUri == $"https://github.com/memton80/quai-pdf/releases/tag/{ReleaseTag}",
            "le bandeau de mise à jour ne s'affiche pas pour une version plus récente");
        Check(!banner.CanInstallInPlace, "release fictive sans installateur : le bouton doit ouvrir la page de la release");
        await Task.Delay(800);
        await CaptureAsync(hwnd, directory, "7-mise-a-jour.png", report);
        banner.IsOpen = false;
        await Task.Delay(300);
        Check(window.Updates.IsOpen == false, "fermeture du bandeau de mise à jour");
        report.Add("Bandeau de mise à jour : OK");
    }

    /// <summary>
    /// Texte : tout sélectionner (surlignage à l'écran), copier dans le presse-papiers de Windows, puis un glisser
    /// simulé sur un mot (mêmes appels que la souris).
    /// </summary>
    private static async Task TextAsync(MainWindow window, DocumentViewModel document, DialogService dialogs, IntPtr hwnd, string directory, List<string> report)
    {
        document.SelectAllTextCommand.Execute(null);
        var all = document.SelectedText;
        Check(all.Contains("Page 1 : Bonjour Quai PDF", StringComparison.Ordinal) && all.Contains("Page 3", StringComparison.Ordinal),
            $"texte sélectionné : « {all} »");
        await Task.Delay(300);
        Check(window.View.HighlightCount > 0, "surlignage du texte sélectionné à l'écran");
        report.Add($"Texte du document : {all.Length} caractères, {window.View.HighlightCount} rectangles surlignés à l'écran");

        // Copie : un message d'erreur (presse-papiers indisponible) ferait attendre la commande.
        var copy = document.CopyTextCommand.ExecuteAsync(null);
        await Task.WhenAny(copy, Task.Delay(3000));
        if (!copy.IsCompleted)
        {
            await CaptureAsync(hwnd, directory, "echec-copie.png", report);
            dialogs.Current?.Hide();
            await copy;
            Check(false, "copie dans le presse-papiers : message d'erreur affiché");
        }

        try
        {
            var copied = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
            Check(copied == all, $"presse-papiers : « {copied} »");
            report.Add("Presse-papiers : texte copié relu à l'identique");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Lecture refusée par Windows (session sans focus) : la copie elle-même n'a pas signalé d'erreur.
            report.Add($"Presse-papiers non relu ({ex.GetType().Name}) : copie faite sans erreur");
        }

        // Glisser du milieu gauche de « Bonjour » à son milieu droit, sur la page 1.
        var page = document.TextPage(0) ?? throw new InvalidOperationException("Texte de la page 1 indisponible.");
        var start = page.AllText().IndexOf("Bonjour", StringComparison.Ordinal);
        Check(start >= 0, "mot « Bonjour » sur la page 1");
        var word = page.Rects(start, start + 6)[0];
        var middle = word.Y + (word.Height / 2);
        document.BeginTextSelection(0, new PdfPoint(word.X + 1, middle), extend: false);
        document.ExtendTextSelection(0, new PdfPoint(word.Right - 1, middle));
        Check(document.SelectedText == "Bonjour", $"glisser sur « Bonjour » : « {document.SelectedText} »");
        await Task.Delay(300);
        await CaptureAsync(hwnd, directory, "2b-texte.png", report);

        document.ClearTextSelection();
        Check(window.View.HighlightCount == 0, "surlignage effacé");
    }

    /// <summary>
    /// Défilement : le numéro de page et la vignette sélectionnée (sa barre d'accent) suivent la page visible, jusqu'à la
    /// dernière ; un « aller à la page » sans défilement (page déjà à l'écran) n'empêche pas de suivre la suite.
    /// </summary>
    private static async Task FollowVisiblePageAsync(MainWindow window, DocumentViewModel document, IntPtr hwnd, string directory, List<string> report)
    {
        Check(window.View.SelectedThumbnails.SequenceEqual([0]), $"vignettes sélectionnées à l'ouverture : [{string.Join(", ", window.View.SelectedThumbnails)}]");

        window.View.ScrollReaderToEnd();
        await WaitForAsync(() => document.CurrentPageNumber == 3 && window.View.ShownPageNumber == 3, "numéro de page après défilement jusqu'en bas");
        await WaitForAsync(() => window.View.SelectedThumbnails.SequenceEqual([2]), "vignette de la page visible après défilement");
        await Task.Delay(500);
        await CaptureAsync(hwnd, directory, "2c-defilement.png", report);

        document.GoToPage(1);
        await WaitForAsync(() => window.View.ShownPageNumber == 1 && window.View.SelectedThumbnails.SequenceEqual([0]), "retour à la page 1");
        await Task.Delay(300);

        // Page 1 déjà en haut de l'écran : rien ne défile. Le défilement suivant doit tout de même être suivi.
        document.GoToPage(1);
        await Task.Delay(300);
        window.View.ScrollReaderToEnd();
        await WaitForAsync(() => document.CurrentPageNumber == 3, "page visible après un « aller à la page » sans défilement");

        document.GoToPage(1);
        await WaitForAsync(() => window.View.ShownPageNumber == 1 && window.View.SelectedThumbnails.SequenceEqual([0]), "retour à la page 1");
        report.Add("Défilement : numéro de page et vignette sélectionnée suivent la page visible");
    }

    /// <summary>
    /// Volets du rail : plan (un chapitre par page, un clic montre sa page), recherche (« bonjour » sur chaque page,
    /// sans tenir compte des majuscules ; l'occurrence montrée est surlignée), propriétés (titre du document).
    /// </summary>
    private static async Task PanelsAsync(MainWindow window, DocumentViewModel document, IntPtr hwnd, string directory, List<string> report)
    {
        var view = window.View;

        view.OpenPanel(SidePanel.Outline);
        await WaitForAsync(() => view.OutlineItemCount == 3, $"plan affiché ({view.OutlineItemCount} entrées)");
        Check(document.Outline.Select(i => i.PageIndex).SequenceEqual([0, 1, 2]), "pages du plan");
        document.GoToOutlineItem(document.Outline[2]);
        await WaitForAsync(() => view.ShownPageNumber == 3, "page du chapitre 3 montrée");
        await Task.Delay(500);
        await CaptureAsync(hwnd, directory, "2d-plan.png", report);

        view.OpenPanel(SidePanel.Search);
        document.SearchQuery = "bonjour";
        await WithTimeout(document.SearchCommand.ExecuteAsync(null), "recherche");
        Check(document.SearchResults.Select(r => r.Page).SequenceEqual([0, 1, 2]), $"recherche : {document.SearchResults.Count} résultats");
        document.NextSearchResultCommand.Execute(null);
        Check(document.CurrentSearchResult?.Page == 1 && document.SelectedText == "Bonjour", $"résultat suivant : « {document.SelectedText} »");
        await WaitForAsync(() => view.ShownPageNumber == 2, "page du résultat montrée");
        await WaitForAsync(() => view.HighlightCount > 0, "occurrence surlignée");
        await Task.Delay(500);
        await CaptureAsync(hwnd, directory, "2e-recherche.png", report);

        view.OpenPanel(SidePanel.Properties);
        await WaitForAsync(() => document.Properties.Any(p => p.Value == "Exemple Quai PDF"), "titre du document dans les propriétés");
        await Task.Delay(500);
        await CaptureAsync(hwnd, directory, "2f-proprietes.png", report);

        document.ClearTextSelection();
        view.OpenPanel(SidePanel.Pages);
        document.GoToPage(1);
        await WaitForAsync(() => view.ShownPageNumber == 1, "retour à la page 1");
        report.Add($"Volets : plan ({view.OutlineItemCount} entrées), recherche ({document.SearchResults.Count} résultats), propriétés ({document.Properties.Count} lignes)");
    }

    /// <summary>
    /// Zones de texte, par le même chemin que la souris et le clavier : l'outil « Texte », l'éditeur posé sur la page,
    /// le texte tapé, puis sa modification, son déplacement, sa suppression et l'annulation. Vérifie aussi que le moteur
    /// de Windows dessine bien l'apparence écrite (pixels sombres là où est la zone).
    /// </summary>
    private static async Task TextBoxesAsync(MainWindow window, DocumentViewModel document, IntPtr hwnd, string directory, List<string> report)
    {
        var view = window.View;
        var place = new PdfPoint(110, 880);
        var before = await DarkPixelsAsync(document, 0, new PdfRect(place.X, place.Y, 300, 40));

        document.IsTextToolActive = true;
        Check(view.TypeInTextBoxEditor(0, null, place, "Zone de texte Quai PDF"), "ouverture de l'éditeur de zone de texte");
        Check(view.IsEditingTextBox, "éditeur de zone de texte affiché");
        await Task.Delay(600);
        await CaptureAsync(hwnd, directory, "2g-zone-texte-saisie.png", report);
        await WithTimeout(view.CommitTextBoxEditorAsync(), "enregistrement de la zone de texte");
        var box = Assert1(document.TextBoxes(0), "zone de texte ajoutée");
        Check(box.Text == "Zone de texte Quai PDF", $"texte de la zone : « {box.Text} »");
        Check(Math.Abs(box.Area.X - place.X) < 1.5 && Math.Abs(box.Area.Y - place.Y) < 1.5, $"place de la zone : {box.Area}");
        Check(document.SelectedTextBox == box, "zone ajoutée sélectionnée");

        // Le moteur de Windows dessine l'apparence écrite par Quai PDF (celle que liront aussi Chrome, Firefox, Acrobat).
        await WaitForAsync(() => window.View.TextBoxOutlineCount > 0, "contour de la zone sélectionnée");
        var after = await DarkPixelsAsync(document, 0, box.Area);
        Check(after > before + 30, $"Windows ne dessine pas la zone de texte ({before} pixels sombres avant, {after} après)");
        await Task.Delay(800);
        await CaptureAsync(hwnd, directory, "2h-zone-texte.png", report);

        // Modification (comme un double-clic), déplacement, suppression, annulation.
        Check(view.TypeInTextBoxEditor(0, box, new PdfPoint(box.Area.X, box.Area.Y), "Zone modifiée\rsur deux lignes"), "éditeur sur la zone existante");
        await WithTimeout(view.CommitTextBoxEditorAsync(), "modification de la zone de texte");
        box = Assert1(document.TextBoxes(0), "zone de texte modifiée");
        Check(box.Text == "Zone modifiée\nsur deux lignes", $"texte modifié : « {box.Text} »");

        await document.SetTextColorAsync(document.TextColors[1].Color);
        box = Assert1(document.TextBoxes(0), "couleur de la zone");
        Check(box.Color.ToHex() == document.TextColors[1].Color.ToHex(), $"couleur de la zone : {box.Color.ToHex()}");

        Check(await document.MoveTextBoxAsync(box, new PdfPoint(140, 940)), "déplacement de la zone de texte");
        box = Assert1(document.TextBoxes(0), "zone déplacée");
        Check(Math.Abs(box.Area.Y - 940) < 1.5, $"zone déplacée : {box.Area}");

        await document.DeleteSelectedTextBoxCommand.ExecuteAsync(null);
        Check(document.TextBoxes(0).Count == 0, "suppression de la zone de texte");
        await document.UndoCommand.ExecuteAsync(null);
        Check(document.TextBoxes(0).Count == 1, "annulation de la suppression de la zone");

        document.IsTextToolActive = false;
        document.SelectTextBox(null);
        report.Add($"Zones de texte : ajout, modification, couleur, déplacement, suppression, annulation ; dessinée par Windows ({before} → {after} pixels sombres) : OK");
    }

    private static PdfTextBox Assert1(IReadOnlyList<PdfTextBox> boxes, string what)
    {
        Check(boxes.Count == 1, $"{what} : {boxes.Count} zone(s) au lieu d'une");
        return boxes[0];
    }

    /// <summary>Pixels sombres d'un rectangle de la page (pixels à 100 %), dessinée par le moteur de Windows.</summary>
    private static async Task<int> DarkPixelsAsync(DocumentViewModel document, int page, PdfRect area)
    {
        var rendered = document.Rendered as WindowsRenderedPdf ?? throw new InvalidOperationException("Moteur d'affichage inattendu.");
        var width = (uint)Math.Round(document.Pages[page].Size.Width);
        var pixels = await rendered.RenderPixelsAsync(page, width) ?? throw new InvalidOperationException("Page non dessinée.");
        var scale = pixels.Width / document.Pages[page].Size.Width;
        var dark = 0;
        for (var y = (int)(area.Y * scale); y < Math.Min(pixels.Height, (int)(area.Bottom * scale)); y++)
        {
            for (var x = (int)(area.X * scale); x < Math.Min(pixels.Width, (int)(area.Right * scale)); x++)
            {
                var i = ((y * pixels.Width) + x) * 4;
                if (pixels.Bgra[i] + pixels.Bgra[i + 1] + pixels.Bgra[i + 2] < 3 * 160)
                {
                    dark++;
                }
            }
        }

        return dark;
    }

    /// <summary>
    /// Instance unique : un second lancement avec un PDF (nom avec une espace, comme depuis l'Explorateur) le transmet
    /// à cette fenêtre, qui l'ouvre dans un onglet, et s'arrête aussitôt. Le second processus hérite du dossier de
    /// données isolé du test, donc de sa clé d'instance.
    /// </summary>
    private static async Task SingleInstanceAsync(WorkspaceViewModel workspace, string directory, List<string> report)
    {
        var path = Path.Combine(directory, "second fichier.pdf");
        File.WriteAllBytes(path, SampleDocument.Create());
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de Quai PDF inconnu."))
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add(path);
        using var second = Process.Start(start) ?? throw new InvalidOperationException("Second lancement impossible.");

        await WaitForAsync(
            () => workspace.Documents.Any(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)),
            "PDF passé à un second lancement, ouvert dans un onglet de cette fenêtre");
        await WaitForAsync(() => second.HasExited, "arrêt du second lancement");
        Check(second.ExitCode == 0, $"second lancement terminé avec le code {second.ExitCode}");
        Check(string.Equals(workspace.Selected.FilePath, path, StringComparison.OrdinalIgnoreCase), "onglet du fichier transmis affiché");
        report.Add($"Instance unique : « {Path.GetFileName(path)} » transmis par un second lancement, ouvert dans un onglet : OK");
    }

    /// <summary>
    /// Fenêtre entière à l'écran (celui du CI ne fait que 1024 × 768) : les captures montrent aussi la barre d'état.
    /// </summary>
    private static void FitToScreen(MainWindow window)
    {
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = window.AppWindow.Size;
        window.AppWindow.MoveAndResize(new RectInt32(area.X, area.Y, Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height)));
    }

    public static void Fail(string directory, Exception? exception)
    {
        TryWrite(Path.Combine(directory, "smoke-error.txt"), exception?.ToString() ?? "Erreur inconnue");
        Environment.Exit(1);
    }

    private static async Task CaptureAsync(IntPtr hwnd, string directory, string name, List<string> report)
    {
        var colors = await ScreenCapture.SaveAsync(hwnd, Path.Combine(directory, name));
        report.Add($"Capture {name} : {colors} couleurs");
        Check(colors > 20, $"capture {name} presque uniforme ({colors} couleurs) : fenêtre vide ?");
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, string step)
    {
        await WithTimeout((Task)task, step);
        return await task;
    }

    private static async Task WithTimeout(Task task, string step)
    {
        if (await Task.WhenAny(task, Task.Delay(StepTimeout)) != task)
        {
            throw new TimeoutException($"Étape « {step} » sans réponse après {StepTimeout.TotalSeconds} s.");
        }

        await task;
    }

    private static async Task WaitForAsync(Func<bool> condition, string step)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Étape « {step} » non atteinte après {StepTimeout.TotalSeconds} s.");
            }

            await Task.Delay(100);
        }
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Vérification en échec : " + what);
        }
    }

    private static string Describe(PdfPageSize size) =>
        string.Create(CultureInfo.InvariantCulture, $"{size.Width:0.#}×{size.Height:0.#}");

    private static void TryWrite(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception)
        {
            // Rien de plus à faire : le code de sortie signale déjà l'échec.
        }
    }

    /// <summary>
    /// Document d'exemple : A4 portrait, A4 paysage, Letter ; une flèche montre le haut de chaque page, une ligne
    /// de texte (police Arial de Windows, intégrée au fichier par PDFsharp) sert à la sélection et à la recherche, et le
    /// plan a un chapitre par page.
    /// </summary>
    private static class SampleDocument
    {
        public static byte[] Create(string? ownerPassword = null)
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            using var document = new PdfDocument();
            document.Info.Title = "Exemple Quai PDF";
            AddPage(document, 1, 595, 842, XColors.SteelBlue);
            AddPage(document, 2, 842, 595, XColors.SeaGreen);
            AddPage(document, 3, 612, 792, XColors.IndianRed);
            if (ownerPassword is not null)
            {
                document.SecuritySettings.OwnerPassword = ownerPassword;
            }

            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }

        private static void AddPage(PdfDocument document, int number, double width, double height, XColor color)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            using var gfx = XGraphics.FromPdfPage(page);
            var brush = new XSolidBrush(color);
            gfx.DrawRectangle(new XPen(color, 4), 36, 36, width - 72, height - 72);
            gfx.DrawPolygon(brush, [new XPoint(width / 2, 90), new XPoint((width / 2) - 60, 190), new XPoint((width / 2) + 60, 190)], XFillMode.Winding);
            gfx.DrawRectangle(brush, (width / 2) - 20, 190, 40, 120);
            gfx.DrawString($"Page {number} : Bonjour Quai PDF", new XFont("Arial", 22), XBrushes.Black, new XPoint(72, 352));
            document.Outlines.Add($"Chapitre {number}", page);
            for (var i = 0; i < 5; i++)
            {
                gfx.DrawRectangle(XBrushes.LightGray, 72, 380 + (i * 30), width - 144 - (i % 3 * 60), 12);
            }
        }
    }
}

/// <summary>Source de releases du test de fumée : une version plus récente, sans fichier, sans requête réseau.</summary>
internal sealed class SmokeReleaseSource : IReleaseSource
{
    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ReleaseInfo?>(new ReleaseInfo(SmokeTest.ReleaseTag, []));
}
