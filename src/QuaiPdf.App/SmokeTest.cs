using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QuaiPdf.App.Services;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Storage;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App;

/// <summary>
/// Test de fumée lancé par le CI : <c>QuaiPdf.exe --smoke-test &lt;dossier&gt;</c>.
/// Ouvre la fenêtre, un document d'exemple, le modifie (rotation, suppression, annulation, ordre), l'enregistre,
/// relit le résultat, ouvre les réglages et un document protégé, enregistre des captures, puis quitte avec le code 0
/// (fichier <c>smoke-ok.txt</c>) ou 1 (fichier <c>smoke-error.txt</c>). Données dans un dossier jetable.
/// </summary>
internal static class SmokeTest
{
    private const string Switch = "--smoke-test";
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

    public static void IsolateData(string directory) =>
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Path.Combine(directory, "data"));

    public static async Task RunAsync(MainWindow window, IServiceProvider services, ILogger logger, string directory)
    {
        var report = new List<string>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(1500);
            await CaptureAsync(hwnd, directory, "1-accueil.png", report);

            var document = services.GetRequiredService<DocumentViewModel>();
            var settings = services.GetRequiredService<SettingsViewModel>();

            // Ouverture : trois pages de tailles différentes, toutes modifiables.
            var sample = Path.Combine(directory, "exemple.pdf");
            File.WriteAllBytes(sample, SampleDocument.Create());
            Check(await WithTimeout(document.OpenFileAsync(sample), "ouverture"), "ouverture du document d'exemple");
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

            report.Add("Tailles des pages (Windows) : " + string.Join(" ; ", document.Pages.Select(p => Describe(p.Size))));

            // Images des pages : dessinées par le moteur PDF de Windows.
            var rendered = document.Rendered as WindowsRenderedPdf ?? throw new InvalidOperationException("Moteur d'affichage inattendu.");
            var bitmap = await rendered.RenderAsync(0, 400);
            Check(bitmap is { PixelWidth: 400 }, $"rendu direct de la page 1 : {bitmap?.PixelWidth} px de large");
            await WaitForAsync(() => window.View.RenderedPageCount > 0, "pages dessinées à l'écran");
            report.Add($"Pages dessinées à l'écran : {window.View.RenderedPageCount}");
            await Task.Delay(800);
            await CaptureAsync(hwnd, directory, "2-document.png", report);

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

            settings.ThemeIndex = 2;
            await Task.Delay(1000);
            await CaptureAsync(hwnd, directory, "5-sombre.png", report);
            settings.ThemeIndex = 0;

            // Document protégé contre la modification : affiché en lecture seule.
            var restricted = Path.Combine(directory, "protege.pdf");
            File.WriteAllBytes(restricted, SampleDocument.Create(ownerPassword: "proprietaire"));
            Check(await WithTimeout(document.OpenFileAsync(restricted), "ouverture du document protégé"), "ouverture du document protégé");
            Check(document.Editability == PdfEditability.Protected, $"document protégé en lecture seule ({document.Editability})");
            Check(!document.RotateRightCommand.CanExecute(null), "rotation désactivée en lecture seule");
            await Task.Delay(1000);
            await CaptureAsync(hwnd, directory, "6-lecture-seule.png", report);

            await document.CloseDocumentCommand.ExecuteAsync(null);
            Check(!document.HasDocument, "fermeture du document");

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
        if (await Task.WhenAny(task, Task.Delay(StepTimeout)) != task)
        {
            throw new TimeoutException($"Étape « {step} » sans réponse après {StepTimeout.TotalSeconds} s.");
        }

        return await task;
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

    /// <summary>Document d'exemple : A4 portrait, A4 paysage, Letter ; une flèche montre le haut de chaque page.</summary>
    private static class SampleDocument
    {
        public static byte[] Create(string? ownerPassword = null)
        {
            using var document = new PdfDocument();
            document.Info.Title = "Exemple Quai PDF";
            AddPage(document, 595, 842, XColors.SteelBlue);
            AddPage(document, 842, 595, XColors.SeaGreen);
            AddPage(document, 612, 792, XColors.IndianRed);
            if (ownerPassword is not null)
            {
                document.SecuritySettings.OwnerPassword = ownerPassword;
            }

            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }

        private static void AddPage(PdfDocument document, double width, double height, XColor color)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            using var gfx = XGraphics.FromPdfPage(page);
            var brush = new XSolidBrush(color);
            gfx.DrawRectangle(new XPen(color, 4), 36, 36, width - 72, height - 72);
            gfx.DrawPolygon(brush, [new XPoint(width / 2, 90), new XPoint((width / 2) - 60, 190), new XPoint((width / 2) + 60, 190)], XFillMode.Winding);
            gfx.DrawRectangle(brush, (width / 2) - 20, 190, 40, 120);
            for (var i = 0; i < 6; i++)
            {
                gfx.DrawRectangle(XBrushes.LightGray, 72, 360 + (i * 30), width - 144 - (i % 3 * 60), 12);
            }
        }
    }
}
