using QuaiPdf.Core.Services;
using QuaiPdf.Core.Tests.Support;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.ViewModels;

public sealed class WorkspaceViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeRenderer _renderer = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeFileDialogs _files = new();
    private readonly List<DocumentViewModel> _created = [];
    private readonly WorkspaceViewModel _workspace;

    public WorkspaceViewModelTests()
    {
        _workspace = new WorkspaceViewModel(
            () =>
            {
                var document = new DocumentViewModel(_renderer, _dialogs, _files, new FakeStrings(), new FakeClipboard());
                _created.Add(document);
                return document;
            },
            _files);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Starts_WithOneHomeTab()
    {
        var home = Assert.Single(_workspace.Documents);
        Assert.Same(home, _workspace.Selected);
        Assert.False(home.HasDocument);
        Assert.Equal("Tab_Home", home.TabTitle);
        Assert.Equal("Quai PDF", _workspace.WindowTitle);
        Assert.False(_workspace.CloseSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task FirstFile_OpensInTheHomeTab()
    {
        var home = _workspace.Selected;

        Assert.True(await _workspace.OpenFileAsync(Write("a.pdf", 2)));

        Assert.Same(home, Assert.Single(_workspace.Documents));
        Assert.Equal("a.pdf", home.TabTitle);
        Assert.Equal("a.pdf - Quai PDF", _workspace.WindowTitle);
        Assert.True(_workspace.CloseSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task NextFiles_OpenInNewTabsAfterTheCurrentOne()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        await _workspace.OpenFileAsync(Write("b.pdf", 2));
        _workspace.Selected = _workspace.Documents[0];

        await _workspace.OpenFileAsync(Write("c.pdf", 3));

        Assert.Equal(["a.pdf", "c.pdf", "b.pdf"], _workspace.Documents.Select(d => d.TabTitle));
        Assert.Equal("c.pdf", _workspace.Selected.TabTitle);
        Assert.Equal(3, _workspace.Selected.PageCount);
    }

    [Fact]
    public async Task FileAlreadyOpen_SelectsItsTab()
    {
        var path = Write("a.pdf", 1);
        await _workspace.OpenFileAsync(path);
        await _workspace.OpenFileAsync(Write("b.pdf", 1));
        var loads = _renderer.Loads;

        Assert.True(await _workspace.OpenFileAsync(path));

        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Equal("a.pdf", _workspace.Selected.TabTitle);
        Assert.Equal(loads, _renderer.Loads);
    }

    [Fact]
    public async Task UnreadableFile_RemovesTheNewTab()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        var first = _workspace.Selected;
        var broken = _temp.File("faux.pdf");
        File.WriteAllBytes(broken, "pas un pdf"u8.ToArray());

        Assert.False(await _workspace.OpenFileAsync(broken));

        Assert.Same(first, Assert.Single(_workspace.Documents));
        Assert.Same(first, _workspace.Selected);
        Assert.Single(_dialogs.Errors);
        Assert.Equal(2, _created.Count);
    }

    [Fact]
    public async Task OpenCommand_OpensEveryPickedFile()
    {
        _files.OpenPaths.AddRange([Write("a.pdf", 1), Write("b.pdf", 2)]);

        await _workspace.OpenCommand.ExecuteAsync(null);

        Assert.Equal(["a.pdf", "b.pdf"], _workspace.Documents.Select(d => d.TabTitle));
        Assert.Equal("b.pdf", _workspace.Selected.TabTitle);
    }

    [Fact]
    public async Task OpenCommand_Cancelled_ChangesNothing()
    {
        await _workspace.OpenCommand.ExecuteAsync(null);

        Assert.Single(_workspace.Documents);
        Assert.False(_workspace.Selected.HasDocument);
    }

    [Fact]
    public async Task CloseTab_SelectsTheNextOneAndDisposes()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        await _workspace.OpenFileAsync(Write("b.pdf", 1));
        await _workspace.OpenFileAsync(Write("c.pdf", 1));
        var b = _workspace.Documents[1];
        var rendered = _renderer.Created[1];

        Assert.True(await _workspace.CloseAsync(b));

        Assert.Equal(["a.pdf", "c.pdf"], _workspace.Documents.Select(d => d.TabTitle));
        Assert.Equal("c.pdf", _workspace.Selected.TabTitle);
        Assert.True(rendered.Disposed);
    }

    [Fact]
    public async Task CloseLastTab_SelectsThePreviousOne()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        await _workspace.OpenFileAsync(Write("b.pdf", 1));

        await _workspace.CloseSelectedCommand.ExecuteAsync(null);

        Assert.Equal("a.pdf", Assert.Single(_workspace.Documents).TabTitle);
        Assert.Same(_workspace.Documents[0], _workspace.Selected);
    }

    [Fact]
    public async Task CloseOnlyTab_GoesBackHome()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        var tab = _workspace.Selected;

        await _workspace.CloseSelectedCommand.ExecuteAsync(null);

        Assert.Same(tab, Assert.Single(_workspace.Documents));
        Assert.False(tab.HasDocument);
        Assert.Equal("Quai PDF", _workspace.WindowTitle);
        Assert.False(_workspace.CloseSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task CloseModifiedTab_Cancelled_KeepsIt()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        await _workspace.OpenFileAsync(Write("b.pdf", 2));
        var b = _workspace.Selected;
        await b.RotateRightCommand.ExecuteAsync(null);
        _workspace.Selected = _workspace.Documents[0];
        _dialogs.SaveChoice = SaveChoice.Cancel;

        Assert.False(await _workspace.CloseAsync(b));

        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Same(b, _workspace.Selected);
        Assert.True(b.IsModified);
    }

    [Fact]
    public async Task ConfirmCloseAll_AsksForEachModifiedDocument()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        await _workspace.Selected.RotateRightCommand.ExecuteAsync(null);
        await _workspace.OpenFileAsync(Write("b.pdf", 1));
        await _workspace.OpenFileAsync(Write("c.pdf", 1));
        await _workspace.Selected.RotateRightCommand.ExecuteAsync(null);
        _dialogs.SaveChoice = SaveChoice.Discard;

        Assert.True(_workspace.HasUnsavedChanges);
        Assert.True(await _workspace.ConfirmCloseAllAsync());

        Assert.Equal(2, _dialogs.SaveQuestions);
    }

    [Fact]
    public async Task ConfirmCloseAll_StopsAtTheFirstCancel()
    {
        await _workspace.OpenFileAsync(Write("a.pdf", 1));
        await _workspace.Selected.RotateRightCommand.ExecuteAsync(null);
        await _workspace.OpenFileAsync(Write("b.pdf", 1));
        await _workspace.Selected.RotateRightCommand.ExecuteAsync(null);
        _dialogs.SaveChoice = SaveChoice.Cancel;

        Assert.False(await _workspace.ConfirmCloseAllAsync());

        Assert.Equal(1, _dialogs.SaveQuestions);
        Assert.Equal("a.pdf", _workspace.Selected.TabTitle);
    }

    [Fact]
    public async Task OpenPaths_ListsFilesInTabOrder()
    {
        var a = Write("a.pdf", 1);
        var b = Write("b.pdf", 1);
        await _workspace.OpenFilesAsync([a, b]);

        Assert.Equal([a, b], _workspace.OpenPaths);
    }

    [Fact]
    public async Task SelectNextAndPrevious_WrapAround()
    {
        await _workspace.OpenFilesAsync([Write("a.pdf", 1), Write("b.pdf", 1), Write("c.pdf", 1)]);

        _workspace.SelectNext();
        Assert.Equal("a.pdf", _workspace.Selected.TabTitle);

        _workspace.SelectPrevious();
        Assert.Equal("c.pdf", _workspace.Selected.TabTitle);
    }

    [Fact]
    public async Task WindowTitle_FollowsTheSelectedDocument()
    {
        await _workspace.OpenFilesAsync([Write("a.pdf", 1), Write("b.pdf", 1)]);
        var changes = new List<string?>();
        _workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await _workspace.Selected.RotateRightCommand.ExecuteAsync(null);
        Assert.Equal("*b.pdf - Quai PDF", _workspace.WindowTitle);
        Assert.Contains(nameof(WorkspaceViewModel.WindowTitle), changes);

        changes.Clear();
        _workspace.Selected = _workspace.Documents[0];
        Assert.Equal("a.pdf - Quai PDF", _workspace.WindowTitle);
        Assert.Contains(nameof(WorkspaceViewModel.WindowTitle), changes);
    }

    private string Write(string name, int pages)
    {
        var path = _temp.File(name);
        File.WriteAllBytes(path, SamplePdf.Create(pages));
        return path;
    }
}
