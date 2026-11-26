using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;

namespace QuaiPdf.Core.Tests.Pdf;

/// <summary>Plan du document (signets) lu par le vrai PDFium.</summary>
public sealed class PdfOutlineTests
{
    [Fact]
    public void Outline_KeepsTitlesPagesAndHierarchy()
    {
        using var text = PdfTextDocument.Open(SamplePdf.CreateWithOutline(), null);

        var outline = text.Outline();

        Assert.Equal(["Introduction", "Conclusion"], outline.Select(i => i.Title));
        Assert.Equal([0, 2], outline.Select(i => i.PageIndex));
        var child = Assert.Single(outline[0].Children);
        Assert.Equal(("Contexte", 1), (child.Title, child.PageIndex));
        Assert.Empty(outline[1].Children);
    }

    [Fact]
    public void Outline_EmptyWithoutBookmarks()
    {
        using var text = PdfTextDocument.Open(SamplePdf.Create(2), null);

        Assert.Empty(text.Outline());
    }

    [Fact]
    public void Outline_AfterDisposal_Throws()
    {
        var text = PdfTextDocument.Open(SamplePdf.CreateWithOutline(), null);
        text.Dispose();

        Assert.Throws<ObjectDisposedException>(() => text.Outline());
    }
}
