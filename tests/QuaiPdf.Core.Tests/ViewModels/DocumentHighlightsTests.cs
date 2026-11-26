using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.ViewModels;

/// <summary>Surlignage dans le document ouvert : outil, texte sélectionné, sélection d'un surlignage, couleur, suppression.</summary>
public sealed class DocumentHighlightsTests : IDisposable
{
    // Première ligne des pages de texte : ligne de base à 700 pt, Helvetica 24.
    private static readonly PdfPoint FirstWord = SamplePdf.TextPoint(78, 708);

    private readonly TempDirectory _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly DocumentViewModel _document;

    public DocumentHighlightsTests()
    {
        _document = new DocumentViewModel(new FakeRenderer(), _dialogs, new FakeFileDialogs(), new FakeStrings(), new FakeClipboard());
    }

    public void Dispose()
    {
        _document.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task SelectedText_IsHighlighted_AndCanBeUndone()
    {
        await OpenTextAsync();
        Assert.False(_document.HighlightSelectedTextCommand.CanExecute(null));
        _document.SelectWordAt(0, FirstWord);
        var selected = _document.TextSelectionRects(0);
        Assert.True(_document.HighlightSelectedTextCommand.CanExecute(null));

        await _document.HighlightSelectedTextCommand.ExecuteAsync(null);

        var highlight = Assert.Single(_document.Highlights(0));
        Assert.Equal(selected.Count, highlight.Areas.Count);
        Assert.Equal(selected[0].X, highlight.Areas[0].X, 1);
        Assert.Equal(selected[0].Width, highlight.Areas[0].Width, 1);
        Assert.False(_document.HasTextSelection);
        Assert.True(_document.IsModified);

        await _document.UndoCommand.ExecuteAsync(null);

        Assert.Empty(_document.Highlights(0));
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task HighlighterTool_HighlightsWhatIsSwept()
    {
        await OpenTextAsync();
        _document.IsHighlightToolActive = true;

        Assert.True(_document.BeginTextSelection(0, SamplePdf.TextPoint(72, 708), extend: false));
        _document.ExtendTextSelection(0, SamplePdf.TextPoint(250, 708));
        Assert.True(await _document.CompleteTextSelectionAsync(0, SamplePdf.TextPoint(250, 708)));

        Assert.Single(_document.Highlights(0));
        Assert.True(_document.IsHighlightToolActive);

        // Sans l'outil, balayer sélectionne seulement.
        _document.IsHighlightToolActive = false;
        _document.BeginTextSelection(0, SamplePdf.TextPoint(72, 678), extend: false);
        _document.ExtendTextSelection(0, SamplePdf.TextPoint(250, 678));
        Assert.False(await _document.CompleteTextSelectionAsync(0, SamplePdf.TextPoint(250, 678)));
        Assert.Single(_document.Highlights(0));
        Assert.True(_document.HasTextSelection);
    }

    [Fact]
    public async Task TurningTheToolOn_HighlightsTheSelectedText()
    {
        await OpenTextAsync();
        _document.SelectWordAt(0, FirstWord);

        _document.IsHighlightToolActive = true;
        await (_document.HighlightSelectedTextCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Single(_document.Highlights(0));
    }

    [Fact]
    public async Task SelectionOverTwoPages_GivesOneHighlightPerPage()
    {
        await OpenTextAsync();
        _document.BeginTextSelection(0, SamplePdf.TextPoint(72, 678), extend: false);
        _document.ExtendTextSelection(1, SamplePdf.TextPoint(150, 708));

        await _document.HighlightSelectionAsync();

        Assert.Single(_document.Highlights(0));
        Assert.Single(_document.Highlights(1));
    }

    [Fact]
    public async Task ClickOnAHighlight_SelectsIt_ThenColorAndDeleteApplyToIt()
    {
        await OpenTextAsync();
        _document.SelectWordAt(0, FirstWord);
        await _document.HighlightSelectionAsync();

        _document.BeginTextSelection(0, FirstWord, extend: false);
        Assert.False(await _document.CompleteTextSelectionAsync(0, FirstWord));
        var selected = _document.SelectedHighlight;
        Assert.NotNull(selected);
        Assert.True(_document.DeleteSelectedHighlightCommand.CanExecute(null));

        var green = _document.HighlightColors[1].Color;
        await _document.SetHighlightColorAsync(green);
        Assert.Equal(green, _document.HighlightColor);
        Assert.NotNull(_document.SelectedHighlight);
        Assert.Single(_document.Highlights(0));
        Assert.Equal(green.ToHex(), AnnotationColor(_document.Content!));

        await _document.DeleteSelectedHighlightCommand.ExecuteAsync(null);
        Assert.Empty(_document.Highlights(0));
        Assert.Null(_document.SelectedHighlight);

        // Un clic à côté ne sélectionne rien.
        _document.BeginTextSelection(0, SamplePdf.TextPoint(400, 300), extend: false);
        await _document.CompleteTextSelectionAsync(0, SamplePdf.TextPoint(400, 300));
        Assert.Null(_document.SelectedHighlight);
    }

    [Fact]
    public async Task NewColor_IsUsedByTheNextHighlights()
    {
        await OpenTextAsync();
        var blue = _document.HighlightColors[2].Color;
        await _document.SetHighlightColorAsync(blue);
        Assert.False(_document.IsModified);

        _document.SelectWordAt(0, FirstWord);
        await _document.HighlightSelectionAsync();

        Assert.Equal(blue.ToHex(), AnnotationColor(_document.Content!));
        Assert.Equal(5, _document.HighlightColors.Count);
        Assert.Equal("HighlightColor_Yellow", _document.HighlightColors[0].Name);
    }

    [Fact]
    public async Task Tools_AreExclusive_AndSelectionsToo()
    {
        await OpenTextAsync();
        _document.IsTextToolActive = true;
        _document.IsHighlightToolActive = true;
        Assert.False(_document.IsTextToolActive);
        _document.IsTextToolActive = true;
        Assert.False(_document.IsHighlightToolActive);

        _document.IsTextToolActive = false;
        _document.SelectWordAt(0, FirstWord);
        await _document.HighlightSelectionAsync();
        await _document.AddTextBoxAsync(0, new PdfPoint(300, 600), "Note");
        _document.SelectHighlight(_document.Highlights(0)[0]);
        Assert.Null(_document.SelectedTextBox);
        _document.SelectTextBox(_document.TextBoxes(0)[0]);
        Assert.Null(_document.SelectedHighlight);
    }

    [Fact]
    public async Task ProtectedDocument_CannotBeHighlighted()
    {
        await OpenTextAsync(ownerPassword: "proprio");

        _document.IsHighlightToolActive = true;
        Assert.False(_document.IsHighlightToolActive);
        _document.SelectWordAt(0, FirstWord);
        Assert.True(_document.HasTextSelection);
        Assert.False(_document.HighlightSelectedTextCommand.CanExecute(null));
        Assert.False(await _document.HighlightSelectionAsync());
    }

    [Fact]
    public async Task SavedFile_KeepsTheHighlight()
    {
        await OpenTextAsync();
        _document.SelectWordAt(0, FirstWord);
        await _document.HighlightSelectionAsync();
        var path = _temp.File("surligne.pdf");

        await _document.SaveToAsync(path);

        using var text = PdfTextDocument.Open(File.ReadAllBytes(path), null);
        Assert.Single(text.GetPage(0).Highlights());
    }

    private static string AnnotationColor(byte[] content) =>
        AnnotatedPdf.Read(content, 0, 0, annotation =>
        {
            var c = annotation.Elements.GetArray("/C")!;
            return new PdfColor(c.Elements.GetReal(0), c.Elements.GetReal(1), c.Elements.GetReal(2)).ToHex();
        });

    private async Task OpenTextAsync(string? ownerPassword = null)
    {
        var path = _temp.File("texte.pdf");
        File.WriteAllBytes(path, SamplePdf.CreateWithText(ownerPassword, true, ["Bonjour tout le monde", "Seconde ligne"], ["Page deux"]));
        Assert.True(await _document.OpenFileAsync(path));
    }
}
