using QuaiPdf.Core.Pdf;

namespace QuaiPdf.Core.Tests.Pdf;

public sealed class PdfHistoryTests
{
    [Fact]
    public void UndoThenRedo_WalkThroughContents()
    {
        var history = new PdfHistory();
        byte[] a = [1], b = [2], c = [3];

        history.Record(a);
        history.Record(b);

        Assert.Same(b, history.UndoTarget);
        history.Undone(c);
        Assert.Same(a, history.UndoTarget);
        Assert.Same(c, history.RedoTarget);

        history.Redone(b);
        Assert.Same(b, history.UndoTarget);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NewChange_ClearsRedo()
    {
        var history = new PdfHistory();
        history.Record([1]);
        history.Undone([2]);

        history.Record([3]);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Capacity_ForgetsOldestSteps()
    {
        var history = new PdfHistory();
        for (var i = 0; i < PdfHistory.Capacity + 5; i++)
        {
            history.Record([(byte)i]);
        }

        var steps = 0;
        while (history.CanUndo)
        {
            history.Undone([0]);
            steps++;
        }

        Assert.Equal(PdfHistory.Capacity, steps);
    }

    [Fact]
    public void Undo_WhenEmpty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new PdfHistory().Undone([1]));
    }
}
