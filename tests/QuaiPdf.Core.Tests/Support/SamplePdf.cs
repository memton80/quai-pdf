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
