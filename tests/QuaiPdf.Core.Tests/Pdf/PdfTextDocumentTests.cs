using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;

namespace QuaiPdf.Core.Tests.Pdf;

/// <summary>Texte lu par le vrai PDFium (bibliothèque native Linux pendant les tests).</summary>
public sealed class PdfTextDocumentTests
{
    private static readonly byte[] TwoPages = SamplePdf.CreateWithText(pages: [["Bonjour le monde", "Deuxieme ligne"], ["Page deux"]]);

    [Fact]
    public void Text_KeepsLinesAndPages()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);

        Assert.Equal(2, text.PageCount);
        Assert.True(text.CanCopy);
        Assert.Equal("Bonjour le monde\r\nDeuxieme ligne", text.GetPage(0).AllText().TrimEnd());
        Assert.Equal("Page deux", text.GetPage(1).AllText().Trim());
    }

    [Fact]
    public void Size_MatchesPageSizesUsedForDisplay()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);

        var expected = PdfOperations.PageSizes(TwoPages)[0];
        Assert.Equal(expected.Width, text.GetPage(0).Size.Width, 1);
        Assert.Equal(expected.Height, text.GetPage(0).Size.Height, 1);
    }

    [Fact]
    public void HitTest_FindsCharacterUnderPoint()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        // « B » de « Bonjour » : un peu à droite du début de la ligne, à mi-hauteur des majuscules.
        Assert.Equal(0, page.HitTest(SamplePdf.TextPoint(78, 708), 2));
        Assert.Equal('D', page.Text(page.HitTest(SamplePdf.TextPoint(78, 678), 2), page.HitTest(SamplePdf.TextPoint(78, 678), 2))[0]);
        Assert.Equal(-1, page.HitTest(SamplePdf.TextPoint(400, 300), 2));
    }

    [Fact]
    public void NearestChar_ToTheRightOfALine_IsItsLastCharacter()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        var index = page.NearestChar(SamplePdf.TextPoint(560, 708));

        Assert.Equal("e", page.Text(index, index));
        Assert.Equal("Bonjour le monde", page.Text(0, index));
    }

    [Fact]
    public void Rects_CoverSelectedWordOnly()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        var rects = page.Rects(0, 6);

        var rect = Assert.Single(rects);
        // Le « B » commence un peu après l'origine du texte (approche de la lettre).
        Assert.InRange(rect.X, 72 * 4.0 / 3, (72 * 4.0 / 3) + 4);
        Assert.InRange(rect.Width, 60, 150);
        Assert.True(rect.Contains(SamplePdf.TextPoint(78, 708)));
        Assert.False(rect.Contains(SamplePdf.TextPoint(78, 678)));
    }

    [Fact]
    public void Rects_SpanningTwoLines_GiveOneRectanglePerLine()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        Assert.Equal(2, page.Rects(0, page.CharCount - 1).Count);
    }

    [Fact]
    public void WordAt_ExpandsToWholeWord()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        var (start, end) = page.WordAt(2);

        Assert.Equal("Bonjour", page.Text(start, end));
        Assert.Equal((7, 7), page.WordAt(7)); // l'espace reste seul
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RotatedPage_MapsPositionsToTheDisplayedPage(int degrees)
    {
        var rotated = PdfOperations.Rotate(TwoPages, [0], degrees);
        using var text = PdfTextDocument.Open(rotated, null);
        var page = text.GetPage(0);

        // Point (x, y) du PDF (origine en bas à gauche, page de 612 × 792 pt) vu sur la page tournée,
        // en points depuis le coin supérieur gauche : un quart de tour à droite met le haut de la page à droite.
        var (x, y) = (78.0, 708.0);
        var (shownX, shownY) = degrees switch
        {
            90 => (y, x),
            180 => (612 - x, y),
            _ => (792 - y, 612 - x),
        };
        var index = page.HitTest(new PdfPoint(shownX * 4 / 3, shownY * 4 / 3), 2);

        Assert.Equal("B", page.Text(index, index));
        var (start, end) = page.WordAt(index);
        Assert.Equal("Bonjour", page.Text(start, end));
        var rect = Assert.Single(page.Rects(start, end));
        Assert.Equal(degrees != 180, rect.Height > rect.Width);
        Assert.True(rect.Contains(new PdfPoint(shownX * 4 / 3, shownY * 4 / 3)));
    }

    [Fact]
    public void PageWithoutText_HasNoCharacters()
    {
        using var text = PdfTextDocument.Open(SamplePdf.Create(1), null);
        var page = text.GetPage(0);

        Assert.Equal(0, page.CharCount);
        Assert.Equal(string.Empty, page.AllText());
        Assert.Equal(-1, page.NearestChar(new PdfPoint(10, 10)));
        Assert.Empty(page.Rects(0, 3));
    }

    [Fact]
    public void PasswordProtected_NeedsPassword()
    {
        var content = SamplePdf.Create(1, userPassword: "secret", ownerPassword: "proprio");

        Assert.Throws<PdfPasswordException>(() => PdfTextDocument.Open(content, null));
        Assert.Throws<PdfPasswordException>(() => PdfTextDocument.Open(content, "faux"));
        using var text = PdfTextDocument.Open(content, "secret");
        Assert.Equal(1, text.PageCount);
    }

    [Fact]
    public void CopyPermission_IsReported()
    {
        var forbidden = SamplePdf.CreateWithText(ownerPassword: "proprio", permitCopy: false, pages: [["Secret"]]);

        using var text = PdfTextDocument.Open(forbidden, null);

        Assert.False(text.CanCopy);
        Assert.Equal("Secret", text.GetPage(0).AllText().Trim());
    }

    [Fact]
    public void Garbage_IsUnreadable()
    {
        var error = Assert.Throws<PdfFileException>(() => PdfTextDocument.Open("pas un pdf"u8.ToArray(), null));
        Assert.Equal(PdfProblem.Unreadable, error.Problem);
    }

    [Fact]
    public void DisposedDocument_ReturnsNothing()
    {
        var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        text.Dispose();

        Assert.Equal(string.Empty, page.AllText());
        Assert.Equal(-1, page.HitTest(SamplePdf.TextPoint(78, 708), 2));
        Assert.Throws<ObjectDisposedException>(() => text.GetPage(1));
    }

    [Theory]
    [InlineData(0, 5, 0, 9, 0, 5, 8)]
    [InlineData(0, 9, 0, 5, 0, 5, 8)]
    [InlineData(0, 5, 2, 3, 1, 0, 19)]
    [InlineData(0, 5, 2, 3, 2, 0, 2)]
    [InlineData(2, 3, 0, 5, 0, 5, 19)]
    [InlineData(0, 0, 0, 20, 0, 0, 19)]
    public void Selection_RangeOnEachPage(int anchorPage, int anchorChar, int focusPage, int focusChar, int page, int from, int to)
    {
        var selection = new PdfTextSelection(new PdfTextPosition(anchorPage, anchorChar), new PdfTextPosition(focusPage, focusChar));

        Assert.Equal((from, to), selection.RangeOn(page, 20));
    }

    [Fact]
    public void Selection_OutsidePagesOrEmptyPage_HasNoRange()
    {
        var selection = new PdfTextSelection(new PdfTextPosition(1, 3), new PdfTextPosition(2, 1));

        Assert.Null(selection.RangeOn(0, 20));
        Assert.Null(selection.RangeOn(3, 20));
        Assert.Null(selection.RangeOn(1, 0));
        Assert.Null(new PdfTextSelection(new PdfTextPosition(0, 20), new PdfTextPosition(1, 0)).RangeOn(0, 20));
    }

    [Fact]
    public void Selection_CollapsedIsEmpty()
    {
        var caret = new PdfTextPosition(0, 4);

        Assert.True(new PdfTextSelection(caret, caret).IsEmpty);
        Assert.Null(new PdfTextSelection(caret, caret).RangeOn(0, 20));
    }

    [Fact]
    public void CaretAt_PicksSideOfCharacter()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        // « B » occupe environ 74 à 87 pt : à gauche de son milieu, devant ; à droite, derrière.
        Assert.Equal(0, page.CaretAt(SamplePdf.TextPoint(76, 708)));
        Assert.Equal(1, page.CaretAt(SamplePdf.TextPoint(86, 708)));
        Assert.Equal(16, page.CaretAt(SamplePdf.TextPoint(560, 708)));
    }

    [Fact]
    public void CaretAt_RightOfShorterLine_StaysOnThatLine()
    {
        using var text = PdfTextDocument.Open(TwoPages, null);
        var page = text.GetPage(0);

        var caret = page.CaretAt(SamplePdf.TextPoint(560, 678));

        Assert.Equal("Bonjour le monde\r\nDeuxieme ligne", page.Text(0, caret - 1));
    }
}
