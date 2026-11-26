using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Pdf.Text;
using QuaiPdf.Core.Tests.Support;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.ViewModels;

/// <summary>Volets du document : plan, recherche, propriétés.</summary>
public sealed class DocumentPanelsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly DocumentViewModel _document;

    public DocumentPanelsTests()
    {
        _document = new DocumentViewModel(new FakeRenderer(), _dialogs, new FakeFileDialogs(), new FakeStrings(), new FakeClipboard());
    }

    public void Dispose()
    {
        _document.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Outline_ListsBookmarksWithTheirPages()
    {
        await OpenAsync("plan.pdf", SamplePdf.CreateWithOutline());

        var outline = _document.Outline;

        Assert.Equal(["Introduction", "Conclusion"], outline.Select(i => i.Title));
        Assert.Equal(["Outline_Page:1", "Outline_Page:3"], outline.Select(i => i.PageLabel));
        Assert.Equal("Contexte", Assert.Single(outline[0].Children).Title);
        Assert.True(outline[0].HasChildren);
    }

    [Fact]
    public async Task GoToOutlineItem_ShowsItsPage()
    {
        await OpenAsync("plan.pdf", SamplePdf.CreateWithOutline());
        int? scrolled = null;
        _document.ScrollRequested += (_, index) => scrolled = index;

        _document.GoToOutlineItem(_document.Outline[1]);

        Assert.Equal(3, _document.CurrentPageNumber);
        Assert.Equal(2, scrolled);
    }

    [Fact]
    public async Task Outline_IsReadAgainAfterAnEdit()
    {
        await OpenAsync("plan.pdf", SamplePdf.CreateWithOutline());
        var before = _document.Outline;

        _document.SetSelection([0]);
        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.NotSame(before, _document.Outline);
    }

    [Fact]
    public async Task Search_FindsOnEveryPageAndShowsTheFirst()
    {
        await OpenTextAsync();
        RevealRequestedEventArgs? revealed = null;
        _document.RevealRequested += (_, e) => revealed = e;
        _document.SearchQuery = "DEUX";

        await _document.SearchCommand.ExecuteAsync(null);

        Assert.Equal([0, 1], _document.SearchResults.Select(r => r.Page));
        Assert.Equal("Search_Count:2", _document.SearchStatus);
        Assert.Same(_document.SearchResults[0], _document.CurrentSearchResult);
        Assert.Equal("Deux", _document.SelectedText);
        Assert.Equal(0, revealed?.Page);
        Assert.True(revealed!.Area.Width > 0);
        Assert.Equal("Page_Name:2", _document.SearchResults[1].PageLabel);
        Assert.Equal(("Page ", "deux"), (_document.SearchResults[1].Before, _document.SearchResults[1].Text));
    }

    [Fact]
    public async Task NextAndPreviousResult_WrapAround()
    {
        await OpenTextAsync();
        _document.SearchQuery = "deux";
        await _document.SearchCommand.ExecuteAsync(null);

        _document.NextSearchResultCommand.Execute(null);
        Assert.Equal(1, _document.CurrentSearchResult?.Page);
        Assert.Equal(2, _document.CurrentPageNumber);

        _document.NextSearchResultCommand.Execute(null);
        Assert.Equal(0, _document.CurrentSearchResult?.Page);

        _document.PreviousSearchResultCommand.Execute(null);
        Assert.Equal(1, _document.CurrentSearchResult?.Page);
    }

    [Fact]
    public async Task Search_NothingFound_SaysSo()
    {
        await OpenTextAsync();
        _document.SearchQuery = "absent";

        await _document.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_document.SearchResults);
        Assert.Equal("Search_None:absent", _document.SearchStatus);
        Assert.False(_document.NextSearchResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task Search_OneResult()
    {
        await OpenTextAsync();
        _document.SearchQuery = "monde";

        await _document.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Search_One", _document.SearchStatus);
        Assert.True(_document.NextSearchResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task Edit_RunsTheSearchAgainWithoutMovingTheReader()
    {
        await OpenTextAsync();
        _document.SearchQuery = "deux";
        await _document.SearchCommand.ExecuteAsync(null);
        _document.NextSearchResultCommand.Execute(null);
        var revealed = 0;
        _document.RevealRequested += (_, _) => revealed++;

        _document.SetSelection([0]);
        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.Equal([0, 1], _document.SearchResults.Select(r => r.Page));
        Assert.Null(_document.CurrentSearchResult);
        Assert.Equal("Search_Count:2", _document.SearchStatus);
        Assert.Equal(0, revealed);
        Assert.False(_document.HasTextSelection);
    }

    [Fact]
    public async Task DeletingAPage_UpdatesTheResults()
    {
        await OpenTextAsync();
        _document.SearchQuery = "deux";
        await _document.SearchCommand.ExecuteAsync(null);

        _document.SetSelection([1]);
        await _document.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([0], _document.SearchResults.Select(r => r.Page));
        Assert.Equal("Search_One", _document.SearchStatus);

        await _document.UndoCommand.ExecuteAsync(null);

        Assert.Equal([0, 1], _document.SearchResults.Select(r => r.Page));
    }

    [Fact]
    public async Task SearchedQuery_IsTheOneRunAgain_NotTheTextBeingTyped()
    {
        await OpenTextAsync();
        _document.SearchQuery = "monde";
        await _document.SearchCommand.ExecuteAsync(null);
        _document.SearchQuery = "deu";

        _document.SetSelection([0]);
        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.Equal("monde", Assert.Single(_document.SearchResults).Text);
    }

    [Fact]
    public async Task OtherDocument_ForgetsTheSearch()
    {
        await OpenTextAsync();
        _document.SearchQuery = "deux";
        await _document.SearchCommand.ExecuteAsync(null);

        await OpenAsync("autre.pdf", SamplePdf.CreateWithText(pages: [["Deux fois deux"]]));
        _document.SetSelection([0]);
        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.Empty(_document.SearchResults);
        Assert.Equal(string.Empty, _document.SearchStatus);
    }

    [Fact]
    public async Task Search_EmptyQuery_DoesNothing()
    {
        await OpenTextAsync();
        _document.SearchQuery = "  ";

        await _document.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_document.SearchResults);
        Assert.Equal(string.Empty, _document.SearchStatus);
    }

    [Fact]
    public void Search_WithoutDocument_IsDisabled()
    {
        Assert.False(_document.SearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Properties_ListMetadataAndFile()
    {
        var path = await OpenAsync("rapport.pdf", SamplePdf.Create(2, title: "Rapport annuel"));

        await _document.RefreshPropertiesAsync();

        var properties = _document.Properties.ToDictionary(p => p.Label, p => p.Value);
        Assert.Equal("Rapport annuel", properties["Metadata_DocumentTitle"]);
        Assert.Equal("—", properties["Metadata_Author"]);
        Assert.Equal("2", properties["Properties_Pages"]);
        Assert.Equal("rapport.pdf", properties["Properties_File"]);
        Assert.Equal(Path.GetDirectoryName(path), properties["Properties_Folder"]);
        Assert.Equal("Properties_Editable", properties["Properties_Editing"]);
        Assert.Equal("Properties_CopyAllowed", properties["Properties_Copy"]);
        Assert.StartsWith("Size_", properties["Properties_Size"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Properties_ProtectedDocument_ShowsFileInformation()
    {
        await OpenAsync("protege.pdf", SamplePdf.CreateWithText(ownerPassword: "proprio", permitCopy: false, pages: [["Secret"]]));

        await _document.RefreshPropertiesAsync();

        var properties = _document.Properties.ToDictionary(p => p.Label, p => p.Value);
        Assert.Equal("Properties_Protected", properties["Properties_Editing"]);
        Assert.Equal("Properties_CopyForbidden", properties["Properties_Copy"]);
        Assert.Equal("protege.pdf", properties["Properties_File"]);
    }

    [Fact]
    public async Task Properties_WithoutDocument_AreEmpty()
    {
        await _document.RefreshPropertiesAsync();

        Assert.Empty(_document.Properties);
    }

    [Theory]
    [InlineData(512, "Size_Bytes:512")]
    [InlineData(2048, "Size_Kilobytes:2")]
    [InlineData(3 * 1024 * 1024, "Size_Megabytes:3")]
    public void FormatSize_PicksTheUnit(long bytes, string expected)
    {
        Assert.Equal(expected, _document.FormatSize(bytes));
    }

    private Task OpenTextAsync() =>
        OpenAsync("texte.pdf", SamplePdf.CreateWithText(pages: [["Bonjour le monde", "Deuxieme ligne"], ["Page deux"]]));

    private async Task<string> OpenAsync(string name, byte[] content)
    {
        var path = _temp.File(name);
        File.WriteAllBytes(path, content);
        Assert.True(await _document.OpenFileAsync(path));
        return path;
    }
}
