using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Tests.Support;

namespace QuaiPdf.Core.Tests.Pdf;

public sealed class PdfOperationsTests
{
    [Fact]
    public void Inspect_ReportsEditableDocument()
    {
        Assert.Equal(PdfEditability.Editable, PdfOperations.Inspect(SamplePdf.Create(2)));
    }

    [Fact]
    public void Inspect_ReportsPasswordProtectedDocument()
    {
        var content = SamplePdf.Create(1, userPassword: "secret", ownerPassword: "owner");

        Assert.Equal(PdfEditability.Protected, PdfOperations.Inspect(content));
    }

    [Fact]
    public void Inspect_ReportsOwnerRestrictedDocument()
    {
        // Pas de mot de passe à l'ouverture, mais modification réservée au propriétaire.
        var content = SamplePdf.Create(1, ownerPassword: "owner");

        Assert.Equal(PdfEditability.Protected, PdfOperations.Inspect(content));
    }

    [Fact]
    public void Inspect_ReportsGarbageAsUnsupported()
    {
        Assert.Equal(PdfEditability.Unsupported, PdfOperations.Inspect("pas un PDF"u8.ToArray()));
    }

    [Fact]
    public void Operations_OnProtectedDocument_ThrowProtected()
    {
        var content = SamplePdf.Create(2, userPassword: "secret", ownerPassword: "owner");

        var error = Assert.Throws<PdfFileException>(() => PdfOperations.Rotate(content, [0], 90));
        Assert.Equal(PdfProblem.Protected, error.Problem);
    }

    [Fact]
    public void Operations_OnGarbage_ThrowUnreadable()
    {
        var error = Assert.Throws<PdfFileException>(() => PdfOperations.PageCount([1, 2, 3]));
        Assert.Equal(PdfProblem.Unreadable, error.Problem);
    }

    [Fact]
    public void PageSizes_AreInPixelsAndFollowRotation()
    {
        var content = PdfOperations.Rotate(SamplePdf.Create(2), [1], 90);

        var sizes = PdfOperations.PageSizes(content);

        Assert.Equal(PdfPageSize.FromPoints(SamplePdf.WidthOf(0), SamplePdf.Height), sizes[0]);
        Assert.Equal(PdfPageSize.FromPoints(SamplePdf.Height, SamplePdf.WidthOf(1)), sizes[1]);
        Assert.Equal(SamplePdf.WidthOf(0) * 96 / 72, sizes[0].Width, 3);
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(-90, 270)]
    [InlineData(180, 180)]
    [InlineData(450, 90)]
    public void Rotate_AccumulatesAndNormalizes(int degrees, int expected)
    {
        var content = PdfOperations.Rotate(SamplePdf.Create(3), [0, 2], degrees);

        Assert.Equal([expected, 0, expected], PdfOperations.PageRotations(content));
    }

    [Fact]
    public void Rotate_FourQuarterTurnsComesBackToZero()
    {
        var content = SamplePdf.Create(1);
        for (var i = 0; i < 4; i++)
        {
            content = PdfOperations.Rotate(content, [0], 90);
        }

        Assert.Equal([0], PdfOperations.PageRotations(content));
    }

    [Fact]
    public void Rotate_RejectsOddAngles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfOperations.Rotate(SamplePdf.Create(1), [0], 45));
    }

    [Fact]
    public void Rotate_RejectsPageOutsideDocument()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfOperations.Rotate(SamplePdf.Create(2), [2], 90));
    }

    [Fact]
    public void Delete_RemovesSelectedPages()
    {
        var content = PdfOperations.Delete(SamplePdf.Create(5), [3, 1]);

        Assert.Equal([0, 2, 4], SamplePdf.Identify(content));
    }

    [Fact]
    public void Delete_RefusesToRemoveEveryPage()
    {
        Assert.Throws<InvalidOperationException>(() => PdfOperations.Delete(SamplePdf.Create(2), [0, 1]));
    }

    [Fact]
    public void Delete_KeepsOriginalUntouched()
    {
        var original = SamplePdf.Create(3);
        var copy = original.ToArray();

        PdfOperations.Delete(original, [0]);

        Assert.Equal(copy, original);
    }

    [Fact]
    public void Reorder_AppliesPermutation()
    {
        var content = PdfOperations.Reorder(SamplePdf.Create(4), [2, 0, 3, 1]);

        Assert.Equal([2, 0, 3, 1], SamplePdf.Identify(content));
    }

    [Fact]
    public void Reorder_KeepsRotation()
    {
        var rotated = PdfOperations.Rotate(SamplePdf.Create(3), [0], 90);

        var content = PdfOperations.Reorder(rotated, [1, 2, 0]);

        Assert.Equal([1, 2, 0], SamplePdf.Identify(content));
        Assert.Equal([0, 0, 90], PdfOperations.PageRotations(content));
    }

    [Theory]
    [InlineData(new[] { 0, 1 })]
    [InlineData(new[] { 0, 1, 1 })]
    [InlineData(new[] { 0, 1, 3 })]
    public void Reorder_RejectsInvalidOrder(int[] order)
    {
        Assert.Throws<ArgumentException>(() => PdfOperations.Reorder(SamplePdf.Create(3), order));
    }

    [Theory]
    [InlineData(new[] { 2 }, -1, new[] { 0, 2, 1, 3, 4 })]
    [InlineData(new[] { 2 }, 1, new[] { 0, 1, 3, 2, 4 })]
    [InlineData(new[] { 0 }, -1, new[] { 0, 1, 2, 3, 4 })]
    [InlineData(new[] { 4 }, 1, new[] { 0, 1, 2, 3, 4 })]
    [InlineData(new[] { 1, 2 }, -1, new[] { 1, 2, 0, 3, 4 })]
    [InlineData(new[] { 1, 2 }, 1, new[] { 0, 3, 1, 2, 4 })]
    [InlineData(new[] { 0, 1, 3 }, -1, new[] { 0, 1, 3, 2, 4 })]
    [InlineData(new[] { 1, 3 }, 1, new[] { 0, 2, 1, 4, 3 })]
    public void MoveOrder_MovesSelectionByOneStep(int[] selected, int offset, int[] expected)
    {
        Assert.Equal(expected, PdfOperations.MoveOrder(5, selected, offset));
    }

    [Fact]
    public void Insert_AddsPagesAtPosition()
    {
        var target = SamplePdf.Create(3);
        var first = SamplePdf.Create(2, firstWidthOffset: 10);
        var second = SamplePdf.Create(1, firstWidthOffset: 20);

        var content = PdfOperations.Insert(target, 1, [first, second]);

        Assert.Equal([0, 10, 11, 20, 1, 2], SamplePdf.Identify(content));
    }

    [Fact]
    public void Insert_AtEndAppends()
    {
        var content = PdfOperations.Insert(SamplePdf.Create(2), 99, [SamplePdf.Create(1, firstWidthOffset: 10)]);

        Assert.Equal([0, 1, 10], SamplePdf.Identify(content));
    }

    [Fact]
    public void Insert_FromProtectedSource_ThrowsProtected()
    {
        var source = SamplePdf.Create(1, userPassword: "secret", ownerPassword: "owner");

        var error = Assert.Throws<PdfFileException>(() => PdfOperations.Insert(SamplePdf.Create(1), 0, [source]));
        Assert.Equal(PdfProblem.Protected, error.Problem);
    }

    [Fact]
    public void Extract_CreatesDocumentWithChosenPagesAndTitle()
    {
        var content = PdfOperations.Extract(SamplePdf.Create(5, title: "Rapport"), [3, 1]);

        Assert.Equal([3, 1], SamplePdf.Identify(content));
        Assert.Equal("Rapport", PdfOperations.ReadMetadata(content).Title);
    }

    [Fact]
    public void Extract_KeepsRotation()
    {
        var rotated = PdfOperations.Rotate(SamplePdf.Create(2), [1], 270);

        var content = PdfOperations.Extract(rotated, [1]);

        Assert.Equal([270], PdfOperations.PageRotations(content));
    }

    [Fact]
    public void Extract_WithoutPages_Throws()
    {
        Assert.Throws<ArgumentException>(() => PdfOperations.Extract(SamplePdf.Create(2), []));
    }

    [Fact]
    public void Metadata_RoundTrips()
    {
        var metadata = new PdfMetadata("Titre é", "Alice", "Sujet", "un, deux");

        var content = PdfOperations.WriteMetadata(SamplePdf.Create(1), metadata);

        Assert.Equal(metadata, PdfOperations.ReadMetadata(content));
    }

    [Fact]
    public void Metadata_OfDocumentWithoutInfo_IsEmpty()
    {
        Assert.Equal(PdfMetadata.Empty, PdfOperations.ReadMetadata(SamplePdf.Create(1)));
    }

    [Fact]
    public void Operations_ChainWithoutLosingPages()
    {
        var content = SamplePdf.Create(4);
        content = PdfOperations.Rotate(content, [0], 90);
        content = PdfOperations.Delete(content, [2]);
        content = PdfOperations.Reorder(content, [2, 1, 0]);
        content = PdfOperations.Insert(content, 0, [SamplePdf.Create(1, firstWidthOffset: 7)]);

        Assert.Equal([7, 3, 1, 0], SamplePdf.Identify(content));
        Assert.Equal([0, 0, 0, 90], PdfOperations.PageRotations(content));
        Assert.Equal(PdfEditability.Editable, PdfOperations.Inspect(content));
    }
}
