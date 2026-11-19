using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace QuaiPdf.Core.Tests.Support;

/// <summary>
/// Documents d'essai fabriqués avec PDFsharp. Chaque page a une largeur propre (100, 110, 120… points) :
/// on reconnaît ainsi une page après une opération, sans police ni texte.
/// </summary>
internal static class SamplePdf
{
    public const double BaseWidth = 100;
    public const double Height = 300;

    public static byte[] Create(int pages, int firstWidthOffset = 0, string? title = null, string? userPassword = null, string? ownerPassword = null)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(WidthOf(i + firstWidthOffset));
            page.Height = XUnit.FromPoint(Height);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.SteelBlue, 10, 10, 20 + i, 40);
        }

        if (title is not null)
        {
            document.Info.Title = title;
        }

        if (userPassword is not null)
        {
            document.SecuritySettings.UserPassword = userPassword;
        }

        if (ownerPassword is not null)
        {
            document.SecuritySettings.OwnerPassword = ownerPassword;
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    public static double WidthOf(int page) => BaseWidth + (10 * page);

    /// <summary>Hauteur des pages de texte (Letter, en points).</summary>
    public const double TextPageHeight = 792;

    /// <summary>
    /// Pages de texte (Letter) en Helvetica 24, police standard que tout lecteur connaît : pas de fichier de police.
    /// Première ligne : ligne de base à 700 pt du bas, à 72 pt du bord gauche ; lignes suivantes 30 pt plus bas.
    /// </summary>
    public static byte[] CreateWithText(string? ownerPassword = null, bool permitCopy = true, params string[][] pages)
    {
        using var document = new PdfDocument();
        var font = new PdfDictionary(document);
        font.Elements["/Type"] = new PdfName("/Font");
        font.Elements["/Subtype"] = new PdfName("/Type1");
        font.Elements["/BaseFont"] = new PdfName("/Helvetica");
        font.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        document.Internals.AddObject(font);

        foreach (var lines in pages)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(TextPageHeight);
            var fonts = new PdfDictionary(document);
            fonts.Elements["/F1"] = font.Reference;
            page.Resources.Elements["/Font"] = fonts;

            var operators = new StringBuilder("BT /F1 24 Tf 30 TL 72 700 Td\n");
            foreach (var line in lines)
            {
                operators.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj T*\n");
            }

            operators.Append("ET");
            var content = page.Contents.AppendContent();
            content.CreateStream(Encoding.ASCII.GetBytes(operators.ToString()));
        }

        if (ownerPassword is not null)
        {
            document.SecuritySettings.OwnerPassword = ownerPassword;
            document.SecuritySettings.PermitExtractContent = permitCopy;
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Point d'une page de texte non tournée, donné en points PDF (origine en bas à gauche).</summary>
    public static QuaiPdf.Core.Pdf.Text.PdfPoint TextPoint(double x, double y) =>
        new(x * 96 / 72, (TextPageHeight - y) * 96 / 72);

    /// <summary>Numéro d'origine de chaque page (d'après sa largeur), dans l'ordre du document.</summary>
    public static IReadOnlyList<int> Identify(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>()
            .Select(p => (int)Math.Round((p.MediaBox.Width - BaseWidth) / 10))
            .ToList();
    }
}
