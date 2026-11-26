using PdfSharp.Pdf;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;

namespace QuaiPdf.Core.Tests.Pdf;

/// <summary>
/// Zones de texte : écrites par PDFsharp, relues et dessinées par le vrai PDFium (celui de Chrome et d'Edge), sur des
/// pages tournées et décalées, et compatibles avec celles de Firefox et d'Acrobat.
/// </summary>
public sealed class PdfTextBoxesTests
{
    private static readonly PdfTextStyle Red18 = new(18, new PdfColor(1, 0, 0));

    // Un pixel près : arrondis du rendu et des conversions de PDFium.
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
        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(rotation, cropLeft, cropBottom), 0, new PdfPoint(50, 60), "Bonjour", Red18);

        var box = Assert.Single(Boxes(content));
        var size = PdfTextBoxes.Measure("Bonjour", 18);
        Assert.Equal(50, box.Area.X, Tolerance);
        Assert.Equal(60, box.Area.Y, Tolerance);
        Assert.Equal(size.Width, box.Area.Width, Tolerance);
        Assert.Equal(size.Height, box.Area.Height, Tolerance);
        Assert.Equal("Bonjour", box.Text);
        Assert.Equal(18, box.FontSize);
        Assert.Equal(new PdfColor(1, 0, 0), box.Color);
        Assert.True(box.CanEditText && box.CanMove && box.CanDelete);
        Assert.Equal((0, 0), (box.Page, box.Index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Add_AppearanceIsDrawnUprightInsideTheBox(int rotation)
    {
        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(rotation, 40, 25), 0, new PdfPoint(30, 80), "WWWWWWWWWW", Red18);

        var box = Assert.Single(Boxes(content));
        var ink = Ink(content);
        Assert.NotNull(ink);
        Assert.True(ink.Value.X >= box.Area.X - Tolerance && ink.Value.Right <= box.Area.Right + Tolerance, $"encre {ink} hors de {box.Area}");
        Assert.True(ink.Value.Y >= box.Area.Y - Tolerance && ink.Value.Bottom <= box.Area.Bottom + Tolerance, $"encre {ink} hors de {box.Area}");

        // Une ligne de texte droite est bien plus large que haute ; couchée, ce serait l'inverse.
        Assert.True(ink.Value.Width > ink.Value.Height * 4, $"texte couché ? encre {ink}");
    }

    [Fact]
    public void Add_KeepsEveryCharacterInTheText()
    {
        const string text = "Été 2026 : 15 € (« ok ») → 東京";

        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(), 0, new PdfPoint(10, 10), text, PdfTextStyle.Default);

        Assert.Equal(text, Assert.Single(Boxes(content)).Text);
        Assert.False(PdfTextBoxes.IsFullySupported(text));
        Assert.True(PdfTextBoxes.IsFullySupported("Été 2026 : 15 € (« ok ») — œuvre"));
        Assert.NotNull(Ink(content));
    }

    [Fact]
    public void Add_OneLineOfTheBoxPerLineOfText()
    {
        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(), 0, new PdfPoint(10, 10), "Ligne 1\r\nLigne 2\rLigne 3\n", PdfTextStyle.Default);

        var box = Assert.Single(Boxes(content));
        Assert.Equal("Ligne 1\nLigne 2\nLigne 3\n", box.Text);
        var lineHeight = 12 * PdfTextBoxes.LineHeight * PdfPageSize.PixelsPerPoint;
        Assert.Equal((4 * lineHeight) + (2 * PdfTextBoxes.Padding * PdfPageSize.PixelsPerPoint), box.Area.Height, Tolerance);
        Assert.True(Ink(content)!.Value.Height > 2 * lineHeight);
    }

    [Fact]
    public void Add_TextWithParenthesesAndBackslashes_StaysReadable()
    {
        const string text = @"f(x) = \sum (a\b)";

        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(), 0, new PdfPoint(10, 10), text, PdfTextStyle.Default);

        Assert.Equal(text, Assert.Single(Boxes(content)).Text);
        Assert.Equal(PdfEditability.Editable, PdfOperations.Inspect(content));
        Assert.NotNull(Ink(content));
    }

    [Fact]
    public void Add_WritesWhatOtherViewersRead()
    {
        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(90), 0, new PdfPoint(10, 10), "Note", Red18);

        AnnotatedPdf.Read(content, 0, 0, annotation =>
        {
            Assert.Equal("/FreeText", annotation.Elements.GetName("/Subtype"));
            Assert.Equal("/Helv 18 Tf 1 0 0 rg", annotation.Elements.GetString("/DA"));
            Assert.Contains("color:#FF0000", annotation.Elements.GetString("/DS"), StringComparison.Ordinal);
            Assert.Equal(90, annotation.Elements.GetInteger("/Rotate"));
            Assert.Equal(PdfTextBoxes.FlagPrint, annotation.Elements.GetInteger("/F"));
            Assert.StartsWith("quaipdf-", annotation.Elements.GetString("/NM"), StringComparison.Ordinal);
            Assert.NotNull(annotation.Elements.GetDictionary("/AP")?.Elements["/N"]);
            return true;
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Add_EmptyText_IsRefused(string text)
    {
        Assert.Throws<ArgumentException>(() => PdfTextBoxes.Add(AnnotatedPdf.Blank(), 0, new PdfPoint(10, 10), text, PdfTextStyle.Default));
    }

    [Fact]
    public void Add_FontSizeOutOfRange_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfTextBoxes.Add(AnnotatedPdf.Blank(), 0, new PdfPoint(10, 10), "x", new PdfTextStyle(500, PdfColor.Black)));
    }

    [Fact]
    public void FirefoxBox_IsReadWithItsStyle()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Coucou", 30, 300, 120, 330);

        var box = Assert.Single(Boxes(content));

        Assert.Equal("Coucou", box.Text);
        Assert.Equal(18, box.FontSize);
        Assert.Equal(new PdfColor(1, 0, 0), box.Color);
        Assert.Equal(30 * PdfPageSize.PixelsPerPoint, box.Area.X, Tolerance);
        Assert.Equal((400 - 330) * PdfPageSize.PixelsPerPoint, box.Area.Y, Tolerance);
        Assert.True(box.CanEditText && box.CanMove && box.CanDelete);
    }

    [Fact]
    public void FirefoxBox_Edited_KeepsItsPlaceAndIdentity()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Coucou", 30, 300, 120, 330);
        var before = Assert.Single(Boxes(content));

        var edited = PdfTextBoxes.Update(content, 0, before.Index, new PdfPoint(before.Area.X, before.Area.Y), "Bonjour à tous", Red18);

        var after = Assert.Single(Boxes(edited));
        Assert.Equal("Bonjour à tous", after.Text);
        Assert.Equal(before.Area.X, after.Area.X, Tolerance);
        Assert.Equal(before.Area.Y, after.Area.Y, Tolerance);
        AnnotatedPdf.Read(edited, 0, 0, annotation =>
        {
            Assert.Equal("Firefox", annotation.Elements.GetString("/T"));
            Assert.Equal("pdfjs_internal_editor_0", annotation.Elements.GetString("/NM"));
            return true;
        });
        Assert.NotNull(Ink(edited));
    }

    [Fact]
    public void AcrobatRichText_IsDroppedWhenEdited()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(
            AnnotatedPdf.Blank(), "Ancien", 30, 300, 120, 330, richText: "<body><p>Ancien</p></body>");

        var edited = PdfTextBoxes.Update(content, 0, 0, new PdfPoint(40, 93), "Nouveau", new PdfTextStyle(14, new PdfColor(0, 0, 1)));

        AnnotatedPdf.Read(edited, 0, 0, annotation =>
        {
            Assert.False(annotation.Elements.ContainsKey("/RC"));
            Assert.Contains("color:#0000FF", annotation.Elements.GetString("/DS"), StringComparison.Ordinal);
            Assert.Contains("14 Tf", annotation.Elements.GetString("/DA"), StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public void Move_PutsTheBoxAtItsNewPlace()
    {
        var content = PdfTextBoxes.Add(AnnotatedPdf.Blank(90), 0, new PdfPoint(20, 20), "Bouge", Red18);

        var moved = PdfTextBoxes.Update(content, 0, 0, new PdfPoint(150, 220), "Bouge", Red18);

        var box = Assert.Single(Boxes(moved));
        Assert.Equal(150, box.Area.X, Tolerance);
        Assert.Equal(220, box.Area.Y, Tolerance);
    }

    [Fact]
    public void Callout_IsShownButNeitherEditedNorMoved()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Rappel", 100, 300, 190, 330, callout: true);

        var box = Assert.Single(Boxes(content));

        Assert.False(box.CanEditText);
        Assert.False(box.CanMove);
        Assert.True(box.CanDelete);
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Update(content, 0, 0, new PdfPoint(box.Area.X, box.Area.Y), "Autre", PdfTextStyle.Default));
        Assert.Empty(Boxes(PdfTextBoxes.Delete(content, 0, 0)));
    }

    [Fact]
    public void LockedBox_CanChangeItsTextButStaysInPlace()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Fixe", 30, 300, 120, 330, flags: 4 | PdfTextBoxes.FlagLocked);
        var box = Assert.Single(Boxes(content));
        Assert.True(box.CanEditText);
        Assert.False(box.CanMove);
        Assert.False(box.CanDelete);

        var edited = PdfTextBoxes.Update(content, 0, 0, new PdfPoint(box.Area.X, box.Area.Y), "Fixe aussi", Red18);

        Assert.Equal("Fixe aussi", Assert.Single(Boxes(edited)).Text);
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Update(content, 0, 0, new PdfPoint(box.Area.X + 30, box.Area.Y), "Fixe", Red18));
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Delete(content, 0, 0));
    }

    [Fact]
    public void LockedContents_CanMoveButNotChangeItsText()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Signé", 30, 300, 120, 330, flags: 4 | PdfTextBoxes.FlagLockedContents);
        var box = Assert.Single(Boxes(content));
        Assert.False(box.CanEditText);
        Assert.True(box.CanMove);

        var moved = PdfTextBoxes.Update(content, 0, 0, new PdfPoint(80, 200), box.Text, new PdfTextStyle(box.FontSize, box.Color));

        Assert.Equal(80, Assert.Single(Boxes(moved)).Area.X, Tolerance);
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Update(content, 0, 0, new PdfPoint(box.Area.X, box.Area.Y), "Autre", Red18));
    }

    [Fact]
    public void HiddenBox_IsNotListed()
    {
        var content = AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Caché", 30, 300, 120, 330, flags: PdfTextBoxes.FlagHidden);

        Assert.Empty(Boxes(content));
    }

    [Fact]
    public void Delete_RemovesThePopupAndReplies_AndKeepsOtherAnnotations()
    {
        var content = AnnotatedPdf.WithPopupAndReply(AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Note", 30, 300, 120, 330));
        Assert.Equal(["/FreeText", "/Popup", "/Text", "/Square"], AnnotatedPdf.Subtypes(content));

        var deleted = PdfTextBoxes.Delete(content, 0, 0);

        Assert.Equal(["/Square"], AnnotatedPdf.Subtypes(deleted));
    }

    [Fact]
    public void Index_IsThePlaceInTheAnnotationList()
    {
        var content = PdfTextBoxes.Add(AnnotatedPdf.WithPopupAndReply(AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Premier", 30, 300, 120, 330)),
            0, new PdfPoint(10, 10), "Dernier", Red18);

        var boxes = Boxes(content);

        Assert.Equal([("Premier", 0), ("Dernier", 4)], boxes.Select(b => (b.Text, b.Index)));
        var edited = PdfTextBoxes.Update(content, 0, 4, new PdfPoint(10, 10), "Dernier modifié", Red18);
        Assert.Equal(["Premier", "Dernier modifié"], Boxes(edited).Select(b => b.Text));
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Update(content, 0, 1, new PdfPoint(10, 10), "Bulle", Red18));
        Assert.Throws<InvalidOperationException>(() => PdfTextBoxes.Delete(content, 0, 9));
    }

    [Fact]
    public void Boxes_FollowTheirPagesThroughPageOperations()
    {
        var two = SamplePdf.Create(2);
        var content = PdfTextBoxes.Add(two, 1, new PdfPoint(10, 10), "Page deux", Red18);

        Assert.Equal("Page deux", Assert.Single(Boxes(PdfOperations.Reorder(content, [1, 0]), 0)).Text);
        Assert.Empty(Boxes(PdfOperations.Reorder(content, [1, 0]), 1));
        Assert.Single(Boxes(PdfOperations.Rotate(content, [1], 90), 1));
        Assert.Single(Boxes(PdfOperations.Extract(content, [1]), 0));
        Assert.Single(Boxes(PdfOperations.Insert(SamplePdf.Create(1), 0, [content]), 1));
        Assert.Single(Boxes(PdfOperations.Delete(content, [0]), 0));
    }

    [Theory]
    [InlineData("/Helv 12 Tf 0 0 1 rg", 12, 0, 0, 1)]
    [InlineData("0.5 g /F1 9 Tf", 9, 0.5, 0.5, 0.5)]
    [InlineData("/Helv 0 Tf 0 g", 12, 0, 0, 0)]
    [InlineData("0 1 1 0 k /Helv 10 Tf", 10, 1, 0, 0)]
    [InlineData("n'importe quoi", 12, 0, 0, 0)]
    [InlineData("/Helv 400 Tf", 96, 0, 0, 0)]
    public void Style_IsReadFromTheDefaultAppearance(string appearance, double size, double r, double g, double b)
    {
        var style = PdfTextStyle.Parse(appearance);

        Assert.Equal(size, style.FontSize);
        Assert.Equal(new PdfColor(r, g, b), style.Color);
    }

    [Fact]
    public void Helvetica_UsesAdobeMetricsAndWindowsCodes()
    {
        Assert.Equal(0.667, Helvetica.Width("A", 1), 3);
        Assert.Equal(1.112, Helvetica.Width("ab", 1), 3);
        Assert.Equal((byte)0x80, Helvetica.Encode('€'));
        Assert.Equal((byte)0xE9, Helvetica.Encode('é'));
        Assert.Null(Helvetica.Encode('→'));
        Assert.Equal("?"u8.ToArray(), Helvetica.EncodeText("→"));
    }

    [Fact]
    public void Measure_GrowsWithTheText()
    {
        var short1 = PdfTextBoxes.Measure("a", 12);
        var long1 = PdfTextBoxes.Measure("une ligne bien plus longue", 12);
        var twoLines = PdfTextBoxes.Measure("a\nb", 12);

        Assert.True(long1.Width > short1.Width);
        Assert.Equal(short1.Height, long1.Height, 3);
        Assert.True(twoLines.Height > short1.Height);
        Assert.Equal("#FF8000", new PdfColor(1, 0.5, 0).ToHex());
    }

    private static IReadOnlyList<PdfTextBox> Boxes(byte[] content, int page = 0)
    {
        using var text = PdfTextDocument.Open(content, null);
        return text.GetPage(page).TextBoxes();
    }

    /// <summary>Rectangle des pixels sombres de la page dessinée par PDFium (annotations comprises), ou <c>null</c>.</summary>
    private static PdfRect? Ink(byte[] content, int page = 0)
    {
        using var text = PdfTextDocument.Open(content, null);
        var (pixels, width, height) = text.GetPage(page).RenderGray();
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width) + x] < 200)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? null : new PdfRect(left, top, right - left + 1, bottom - top + 1);
    }
}
