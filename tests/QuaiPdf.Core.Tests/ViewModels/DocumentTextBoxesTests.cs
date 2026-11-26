using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.ViewModels;

/// <summary>Zones de texte dans le document ouvert : ajout, modification, déplacement, suppression, annulation.</summary>
public sealed class DocumentTextBoxesTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly DocumentViewModel _document;

    public DocumentTextBoxesTests()
    {
        _document = new DocumentViewModel(new FakeRenderer(), _dialogs, new FakeFileDialogs(), new FakeStrings(), new FakeClipboard());
    }

    public void Dispose()
    {
        _document.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Add_PutsASelectedBoxThatCanBeUndone()
    {
        await OpenAsync(AnnotatedPdf.Blank());

        Assert.True(await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Note"));

        var box = Assert.Single(_document.TextBoxes(0));
        Assert.Equal("Note", box.Text);
        Assert.Equal(box, _document.SelectedTextBox);
        Assert.True(_document.IsModified);
        Assert.True(_document.DeleteSelectedTextBoxCommand.CanExecute(null));

        await _document.UndoCommand.ExecuteAsync(null);

        Assert.Empty(_document.TextBoxes(0));
        Assert.Null(_document.SelectedTextBox);
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task Add_EmptyText_DoesNothing()
    {
        await OpenAsync(AnnotatedPdf.Blank());

        Assert.False(await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "  \n"));

        Assert.Empty(_document.TextBoxes(0));
        Assert.False(_document.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Add_UsesTheChosenStyle()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.SetTextSizeAsync(24);
        await _document.SetTextColorAsync(_document.TextColors[2].Color);

        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Bleu");

        var box = Assert.Single(_document.TextBoxes(0));
        Assert.Equal(24, box.FontSize);
        Assert.Equal(_document.TextColors[2].Color.ToHex(), box.Color.ToHex());
        Assert.Equal(5, _document.TextColors.Count);
        Assert.Equal("TextColor_Black", _document.TextColors[0].Name);
    }

    [Fact]
    public async Task Add_StaysOnThePage()
    {
        await OpenAsync(AnnotatedPdf.Blank());

        await _document.AddTextBoxAsync(0, new PdfPoint(-50, -20), "Bord");

        var box = Assert.Single(_document.TextBoxes(0));
        Assert.Equal(0, box.Area.X, 1);
        Assert.Equal(0, box.Area.Y, 1);
    }

    [Fact]
    public async Task Update_ChangesTheTextAndKeepsTheSelection()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Avant");
        var box = _document.SelectedTextBox!;

        Assert.True(await _document.UpdateTextBoxAsync(box, "Après\nsur deux lignes", new PdfTextStyle(box.FontSize, box.Color)));

        var updated = Assert.Single(_document.TextBoxes(0));
        Assert.Equal("Après\nsur deux lignes", updated.Text);
        Assert.Equal(updated, _document.SelectedTextBox);
        Assert.Equal(20, updated.Area.X, 1);
    }

    [Fact]
    public async Task Update_WithTheSameText_RecordsNothing()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Pareil");
        var box = _document.SelectedTextBox!;
        await _document.SaveToAsync(_temp.File("enregistre.pdf"));

        Assert.False(await _document.UpdateTextBoxAsync(box, "Pareil", new PdfTextStyle(box.FontSize, box.Color)));

        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task Update_WithEmptyText_DeletesTheBox()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Bientôt vide");

        Assert.True(await _document.UpdateTextBoxAsync(_document.SelectedTextBox!, " ", PdfTextStyle.Default));

        Assert.Empty(_document.TextBoxes(0));
        Assert.Null(_document.SelectedTextBox);
    }

    [Fact]
    public async Task Move_PutsTheBoxElsewhere_ButNeverOffThePage()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Bouge");

        Assert.True(await _document.MoveTextBoxAsync(_document.SelectedTextBox!, new PdfPoint(100, 200)));
        Assert.Equal((100d, 200d), Round(Assert.Single(_document.TextBoxes(0)).Area));

        var box = _document.SelectedTextBox!;
        Assert.True(await _document.MoveTextBoxAsync(box, new PdfPoint(10_000, 10_000)));
        var page = _document.Pages[0].Size;
        var moved = Assert.Single(_document.TextBoxes(0)).Area;
        Assert.Equal(page.Width - box.Area.Width, moved.X, 1);
        Assert.Equal(page.Height - box.Area.Height, moved.Y, 1);
    }

    [Fact]
    public async Task Move_ToTheSamePlace_RecordsNothing()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Immobile");
        var box = _document.SelectedTextBox!;
        await _document.SaveToAsync(_temp.File("enregistre.pdf"));

        Assert.False(await _document.MoveTextBoxAsync(box, new PdfPoint(box.Area.X + 0.2, box.Area.Y)));
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task DeleteCommand_RemovesTheSelectedBox()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "À jeter");

        await _document.DeleteSelectedTextBoxCommand.ExecuteAsync(null);

        Assert.Empty(_document.TextBoxes(0));
        Assert.False(_document.DeleteSelectedTextBoxCommand.CanExecute(null));
    }

    [Fact]
    public async Task NewSize_AppliesToTheSelectedBox()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Grandit");
        var before = _document.SelectedTextBox!.Area;

        await _document.SetTextSizeAsync(32);

        var box = Assert.Single(_document.TextBoxes(0));
        Assert.Equal(32, box.FontSize);
        Assert.True(box.Area.Height > before.Height);
        Assert.Equal(box, _document.SelectedTextBox);
    }

    [Fact]
    public async Task NewColor_WithoutSelection_OnlyChangesTheNextBoxes()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Noir");
        _document.SelectTextBox(null);

        await _document.SetTextColorAsync(new PdfColor(1, 0, 0));

        Assert.Equal(PdfColor.Black, Assert.Single(_document.TextBoxes(0)).Color);
        Assert.Equal(new PdfColor(1, 0, 0), _document.TextStyle.Color);
    }

    [Fact]
    public async Task TextBoxAt_FindsTheTopmostBox()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Dessous");
        await _document.AddTextBoxAsync(0, new PdfPoint(25, 35), "Dessus");

        Assert.Equal("Dessus", _document.TextBoxAt(0, new PdfPoint(30, 40))?.Text);
        Assert.Equal("Dessous", _document.TextBoxAt(0, new PdfPoint(21, 31))?.Text);
        Assert.Null(_document.TextBoxAt(0, new PdfPoint(300, 400)));
    }

    [Fact]
    public async Task TextTool_StaysOnDuringEdits_AndIsOffForProtectedDocuments()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        _document.IsTextToolActive = true;

        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Un");
        Assert.True(_document.IsTextToolActive);

        _dialogs.SaveChoice = Services.SaveChoice.Discard;
        await OpenAsync(SamplePdf.Create(1, ownerPassword: "proprio"), "protege.pdf");
        Assert.False(_document.IsTextToolActive);

        _document.IsTextToolActive = true;
        Assert.False(_document.IsTextToolActive);
    }

    [Fact]
    public async Task LockedBox_CannotBeMovedNorDeleted()
    {
        await OpenAsync(AnnotatedPdf.WithFirefoxFreeText(AnnotatedPdf.Blank(), "Verrou", 30, 300, 120, 330, flags: 4 | PdfTextBoxes.FlagLocked));
        var box = Assert.Single(_document.TextBoxes(0));

        Assert.False(await _document.MoveTextBoxAsync(box, new PdfPoint(0, 0)));
        Assert.False(await _document.DeleteTextBoxAsync(box));
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task SavedFile_KeepsTheBox()
    {
        await OpenAsync(AnnotatedPdf.Blank());
        await _document.AddTextBoxAsync(0, new PdfPoint(20, 30), "Gardée");
        var path = _temp.File("garde.pdf");

        await _document.SaveToAsync(path);

        using var text = PdfTextDocument.Open(File.ReadAllBytes(path), null);
        Assert.Equal("Gardée", Assert.Single(text.GetPage(0).TextBoxes()).Text);
    }

    private static (double, double) Round(PdfRect area) => (Math.Round(area.X), Math.Round(area.Y));

    private async Task OpenAsync(byte[] content, string name = "zones.pdf")
    {
        var path = _temp.File(name);
        File.WriteAllBytes(path, content);
        Assert.True(await _document.OpenFileAsync(path));
    }
}
