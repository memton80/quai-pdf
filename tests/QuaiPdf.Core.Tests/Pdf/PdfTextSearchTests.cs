using System.Globalization;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.Tests.Pdf;

public sealed class PdfTextSearchTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    [Fact]
    public void Find_IgnoresCase()
    {
        Assert.Equal([new PdfTextMatch(11, 5)], PdfTextSearch.Find("Bonjour le monde", "MONDE", culture: French));
    }

    [Fact]
    public void Find_IgnoresAccents()
    {
        var text = "Une École et une ecole";

        Assert.Equal([new PdfTextMatch(4, 5), new PdfTextMatch(17, 5)], PdfTextSearch.Find(text, "école", culture: French));
    }

    [Fact]
    public void Find_ExpressionCutAtEndOfLine()
    {
        var text = "Bonjour le monde\r\nDeuxieme ligne";

        var match = Assert.Single(PdfTextSearch.Find(text, "monde deuxième", culture: French));

        Assert.Equal(11, match.Start);
        Assert.Equal("monde\r\nDeuxieme", text.Substring(match.Start, match.Length));
    }

    [Fact]
    public void Find_EveryOccurrenceWithoutOverlap()
    {
        Assert.Equal([0, 2], PdfTextSearch.Find("aaaaa", "aa", culture: French).Select(m => m.Start));
    }

    [Fact]
    public void Find_StopsAtTheLimit()
    {
        Assert.Equal(2, PdfTextSearch.Find("a a a a", "a", max: 2, culture: French).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_EmptyQuery_FindsNothing(string query)
    {
        Assert.Empty(PdfTextSearch.Find("Bonjour", query, culture: French));
    }

    [Fact]
    public void Find_TrimsTheQuery()
    {
        Assert.Single(PdfTextSearch.Find("Bonjour le monde", "  monde ", culture: French));
    }

    [Fact]
    public void Excerpt_CutsAtWordsOnOneLine()
    {
        var text = "Premier paragraphe assez long pour couper\r\nle passage cherché ici et encore une suite de mots qui dépasse";
        var match = Assert.Single(PdfTextSearch.Find(text, "cherché", culture: French));

        var (before, found, after) = PdfTextSearch.Excerpt(text, match, context: 20);

        Assert.Equal("cherché", found);
        Assert.Equal("…couper le passage ", before);
        Assert.Equal(" ici et encore une…", after);
    }

    [Fact]
    public void Excerpt_AtTheEdges_HasNoEllipsis()
    {
        var text = "Bonjour le monde";
        var match = Assert.Single(PdfTextSearch.Find(text, "le", culture: French));

        Assert.Equal(("Bonjour ", "le", " monde"), PdfTextSearch.Excerpt(text, match));
    }
}
