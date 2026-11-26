using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;

namespace QuaiPdf.Core.Tests.Pdf;

/// <summary>
/// Surlignages : écrits par PDFsharp, relus et dessinés par le vrai PDFium (celui de Chrome et d'Edge), sur des pages
/// tournées et décalées ; la couleur teinte le papier sans cacher le texte, comme un surligneur.
/// </summary>
public sealed class PdfHighlightsTests
{
    private static readonly PdfColor Yellow = PdfColor.FromBytes(0xFF, 0xEE, 0x58);

    // Un pixel près : arrondis des conversions de PDFium.
    private const double Tolerance = 1;

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(90, 0, 0)]
    [InlineData(180, 0, 0)]
    [InlineData(270, 0, 0)]
    [InlineData(0, 40, 25)]
    [InlineData(90, 40, 25)]
    [InlineData(270, 40, 25)]
    public void Add_IsReadBackWhereItWasPut(int rotation, double cropLeft, double cropBottom)
    {
        PdfRect[] areas = [new(30, 40, 100, 20), new(30, 70, 60, 20)];

        var content = PdfHighlights.Add(AnnotatedPdf.Blank(rotation, cropLeft, cropBottom), [new PdfHighlightSpan(0, areas)], Yellow);

        var highlight = Assert.Single(Highlights(content));
        Assert.Equal((0, 0), (highlight.Page, highlight.Index));
        Assert.True(highlight.CanEdit);
        Assert.Equal(2, highlight.Areas.Count);
        for (var i = 0; i < areas.Length; i++)
        {
            Assert.Equal(areas[i].X, highlight.Areas[i].X, Tolerance);
            Assert.Equal(areas[i].Y, highlight.Areas[i].Y, Tolerance);
            Assert.Equal(areas[i].Width, highlight.Areas[i].Width, Tolerance);
            Assert.Equal(areas[i].Height, highlight.Areas[i].Height, Tolerance);
        }

        Assert.True(highlight.Contains(new PdfPoint(100, 50)));
        Assert.False(highlight.Contains(new PdfPoint(100, 80)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Add_TintsThePaperWithoutHidingTheText(int rotation)
    {
        var content = PdfOperations.Rotate(SamplePdf.CreateWithText(pages: [["Bonjour tout le monde"]]), [0], rotation);
        var word = TextRects(content, 0, 6);
        var area = Bounds(word);
        var (darkBefore, paperBefore) = Measure(content, area);

        var highlighted = PdfHighlights.Add(content, [new PdfHighlightSpan(0, word)], Yellow);

        var (darkAfter, paperAfter) = Measure(highlighted, area);
        Assert.True(darkBefore > 20, $"texte absent de la zone ({darkBefore} pixels sombres)");
        Assert.True(darkAfter >= darkBefore * 0.9, $"texte caché par la couleur : {darkBefore} → {darkAfter} pixels sombres");
        Assert.True(paperBefore > 0.5, $"papier déjà teinté ({paperBefore:P0} de blanc)");
        Assert.True(paperAfter < 0.02, $"papier pas teinté ({paperAfter:P0} de blanc)");

        // La couleur reste dans la zone : le papier autour n'est pas teinté.
        var outside = area with { X = area.X + 30, Y = area.Bottom + 40 };
        Assert.Equal(Measure(content, outside).White, Measure(highlighted, outside).White, 3);
    }

    [Fact]
    public void Add_WritesWhatOtherViewersRead()
    {
        var content = PdfHighlights.Add(AnnotatedPdf.Blank(), [new PdfHighlightSpan(0, [new PdfRect(30, 40, 100, 20), new PdfRect(30, 70, 60, 20)])], Yellow);

        AnnotatedPdf.Read(content, 0, 0, annotation =>
        {
            Assert.Equal("/Highlight", annotation.Elements.GetName("/Subtype"));
            Assert.Equal(PdfTextBoxes.FlagPrint, annotation.Elements.GetInteger("/F"));
            Assert.StartsWith("quaipdf-", annotation.Elements.GetString("/NM"), StringComparison.Ordinal);
            var color = annotation.Elements.GetArray("/C")!;
            Assert.Equal([1, 0.9333, 0.3451], Enumerable.Range(0, 3).Select(i => Math.Round(color.Elements.GetReal(i), 4)));

            // Haut gauche, haut droite, bas gauche, bas droite, dans le repère du PDF (page de 400 points de haut).
            var quads = annotation.Elements.GetArray("/QuadPoints")!;
            Assert.Equal(16, quads.Elements.Count);
            Assert.Equal([22.5, 370, 97.5, 370, 22.5, 355, 97.5, 355], Enumerable.Range(0, 8).Select(i => Math.Round(quads.Elements.GetReal(i), 4)));

            var form = (PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)annotation.Elements.GetDictionary("/AP")!.Elements["/N"]!).Value;
            var state = form.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/ExtGState")!.Elements.GetDictionary("/GS0")!;
            Assert.Equal("/Multiply", state.Elements.GetName("/BM"));
            return true;
        });
    }

    [Fact]
    public void Add_OneHighlightPerPage_AndSkipsEmptyPages()
    {
        var content = PdfHighlights.Add(
            SamplePdf.Create(3),
            [new PdfHighlightSpan(0, [new PdfRect(5, 5, 20, 10)]), new PdfHighlightSpan(1, []), new PdfHighlightSpan(2, [new PdfRect(5, 5, 20, 10)])],
            Yellow);

        Assert.Single(Highlights(content, 0));
        Assert.Empty(Highlights(content, 1));
        Assert.Single(Highlights(content, 2));
        Assert.Throws<ArgumentException>(() => PdfHighlights.Add(SamplePdf.Create(1), [new PdfHighlightSpan(0, [new PdfRect(5, 5, 0, 10)])], Yellow));
    }

    [Fact]
    public void SetColor_RepaintsTheSameArea_AndKeepsItsIdentity()
    {
        var content = PdfHighlights.Add(AnnotatedPdf.Blank(90), [new PdfHighlightSpan(0, [new PdfRect(30, 40, 100, 20)])], Yellow);
        var before = Assert.Single(Highlights(content));
        var name = AnnotatedPdf.Read(content, 0, 0, a => a.Elements.GetString("/NM"));

        var green = PdfHighlights.SetColor(content, 0, 0, new PdfColor(0.5, 1, 0.5));

        var after = Assert.Single(Highlights(green));
        Assert.Equal(before.Areas[0].X, after.Areas[0].X, Tolerance);
        Assert.Equal(before.Areas[0].Y, after.Areas[0].Y, Tolerance);
        AnnotatedPdf.Read(green, 0, 0, annotation =>
        {
            Assert.Equal(name, annotation.Elements.GetString("/NM"));
            Assert.Equal(0.5, annotation.Elements.GetArray("/C")!.Elements.GetReal(0));
            return true;
        });
    }

    [Fact]
    public void HighlightWithoutQuadPoints_UsesItsRectangle()
    {
        var content = Edit(PdfHighlights.Add(AnnotatedPdf.Blank(), [new PdfHighlightSpan(0, [new PdfRect(30, 40, 100, 20)])], Yellow),
            annotation => annotation.Elements.Remove("/QuadPoints"));

        var highlight = Assert.Single(Highlights(content));
        Assert.Equal(30, highlight.Areas[0].X, Tolerance);
        Assert.Equal(100, highlight.Areas[0].Width, Tolerance);

        var recolored = PdfHighlights.SetColor(content, 0, 0, new PdfColor(1, 0.6, 0.8));
        Assert.Equal(30, Assert.Single(Highlights(recolored)).Areas[0].X, Tolerance);
    }

    [Fact]
    public void Delete_RemovesThePopupAndReplies_AndKeepsOtherAnnotations()
    {
        var content = AnnotatedPdf.WithPopupAndReply(
            PdfHighlights.Add(AnnotatedPdf.Blank(), [new PdfHighlightSpan(0, [new PdfRect(30, 40, 100, 20)])], Yellow));
        Assert.Equal(["/Highlight", "/Popup", "/Text", "/Square"], AnnotatedPdf.Subtypes(content));

        Assert.Equal(["/Square"], AnnotatedPdf.Subtypes(PdfHighlights.Delete(content, 0, 0)));
    }

    [Fact]
    public void LockedHighlight_IsShownButNeitherRecoloredNorDeleted()
    {
        var content = Edit(PdfHighlights.Add(AnnotatedPdf.Blank(), [new PdfHighlightSpan(0, [new PdfRect(30, 40, 100, 20)])], Yellow),
            annotation => annotation.Elements["/F"] = new PdfInteger(PdfTextBoxes.FlagPrint | PdfTextBoxes.FlagLocked));

        Assert.False(Assert.Single(Highlights(content)).CanEdit);
        Assert.Throws<InvalidOperationException>(() => PdfHighlights.SetColor(content, 0, 0, Yellow));
        Assert.Throws<InvalidOperationException>(() => PdfHighlights.Delete(content, 0, 0));
    }

    [Fact]
    public void HiddenHighlight_IsNotListed()
    {
        var content = Edit(PdfHighlights.Add(AnnotatedPdf.Blank(), [new PdfHighlightSpan(0, [new PdfRect(30, 40, 100, 20)])], Yellow),
            annotation => annotation.Elements["/F"] = new PdfInteger(PdfTextBoxes.FlagHidden));

        Assert.Empty(Highlights(content));
    }

    [Fact]
    public void OnlyHighlightsAreChanged()
    {
        var content = PdfHighlights.Add(
            AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Note", 30, 300, 120, 330),
            [new PdfHighlightSpan(0, [new PdfRect(30, 200, 100, 20)])],
            Yellow);

        Assert.Equal(1, Assert.Single(Highlights(content)).Index);
        Assert.Throws<InvalidOperationException>(() => PdfHighlights.Delete(content, 0, 0));
        Assert.Throws<InvalidOperationException>(() => PdfHighlights.SetColor(content, 0, 5, Yellow));
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Delete(content, 0, 1));
        Assert.Equal(["/FreeText"], AnnotatedPdf.Subtypes(PdfHighlights.Delete(content, 0, 1)));
    }

    [Fact]
    public void Highlights_FollowTheirPagesThroughPageOperations()
    {
        var content = PdfHighlights.Add(SamplePdf.Create(2), [new PdfHighlightSpan(1, [new PdfRect(5, 5, 20, 10)])], Yellow);

        Assert.Single(Highlights(PdfOperations.Reorder(content, [1, 0]), 0));
        Assert.Single(Highlights(PdfOperations.Rotate(content, [1], 90), 1));
        Assert.Single(Highlights(PdfOperations.Extract(content, [1]), 0));
        Assert.Single(Highlights(PdfOperations.Delete(content, [0]), 0));
    }

    private static IReadOnlyList<PdfHighlight> Highlights(byte[] content, int page = 0)
    {
        using var text = PdfTextDocument.Open(content, null);
        return text.GetPage(page).Highlights();
    }

    private static IReadOnlyList<PdfRect> TextRects(byte[] content, int from, int to)
    {
        using var text = PdfTextDocument.Open(content, null);
        return text.GetPage(0).Rects(from, to);
    }

    private static PdfRect Bounds(IReadOnlyList<PdfRect> rects)
    {
        var left = rects.Min(r => r.X);
        var top = rects.Min(r => r.Y);
        return new PdfRect(left, top, rects.Max(r => r.Right) - left, rects.Max(r => r.Bottom) - top);
    }

    /// <summary>Dans une zone de la page dessinée par PDFium : pixels d'encre, et part de papier resté blanc.</summary>
    private static (int Dark, double White) Measure(byte[] content, PdfRect area)
    {
        using var text = PdfTextDocument.Open(content, null);
        var (pixels, width, height) = text.GetPage(0).RenderGray();
        int dark = 0, white = 0, total = 0;
        for (var y = (int)Math.Ceiling(area.Y) + 1; y < Math.Min(height, (int)Math.Floor(area.Bottom) - 1); y++)
        {
            for (var x = (int)Math.Ceiling(area.X) + 1; x < Math.Min(width, (int)Math.Floor(area.Right) - 1); x++)
            {
                var value = pixels[(y * width) + x];
                total++;
                if (value < 100)
                {
                    dark++;
                }
                else if (value == 255)
                {
                    white++;
                }
            }
        }

        return (dark, total == 0 ? 0 : white / (double)total);
    }

    /// <summary>Modifie la première annotation de la page, comme l'aurait écrite un autre logiciel.</summary>
    private static byte[] Edit(byte[] content, Action<PdfDictionary> edit)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        var item = document.Pages[0].Elements.GetArray("/Annots")!.Elements[0];
        edit((PdfDictionary)(item is PdfSharp.Pdf.Advanced.PdfReference r ? r.Value : item));
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }
}
