using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.Tests.Support;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.ViewModels;

public sealed class DocumentViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeRenderer _renderer = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeFileDialogs _files = new();
    private readonly DocumentViewModel _document;

    public DocumentViewModelTests()
    {
        _document = new DocumentViewModel(_renderer, _dialogs, _files, new FakeStrings());
    }

    public void Dispose()
    {
        _document.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Open_ShowsPagesWithoutModification()
    {
        var path = Write("rapport.pdf", SamplePdf.Create(3));

        Assert.True(await _document.OpenFileAsync(path));

        Assert.True(_document.HasDocument);
        Assert.Equal(3, _document.PageCount);
        Assert.Equal([1, 2, 3], _document.Pages.Select(p => p.Number));
        Assert.Equal(PdfEditability.Editable, _document.Editability);
        Assert.True(_document.CanEdit);
        Assert.False(_document.IsModified);
        Assert.Equal("rapport.pdf - Quai PDF", _document.WindowTitle);
        Assert.Equal(1, _document.CurrentPageNumber);
        Assert.False(_document.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Open_ZoomsToFitWidthWithinLimit()
    {
        _document.ViewportWidth = 48 + (SamplePdf.WidthOf(0) * 96 / 72);

        await _document.OpenFileAsync(Write("a.pdf", SamplePdf.Create(1)));

        Assert.Equal(1, _document.Zoom, 3);
    }

    [Fact]
    public async Task OpenCommand_UsesPicker()
    {
        _files.OpenPath = Write("a.pdf", SamplePdf.Create(2));

        await _document.OpenCommand.ExecuteAsync(null);

        Assert.Equal(_files.OpenPath, _document.FilePath);
    }

    [Fact]
    public async Task Open_UnreadableFile_ShowsErrorAndKeepsNothing()
    {
        var path = Write("faux.pdf", "pas un pdf"u8.ToArray());
        var renderer = new ThrowingRenderer();
        using var document = new DocumentViewModel(renderer, _dialogs, _files, new FakeStrings());

        Assert.False(await document.OpenFileAsync(path));

        Assert.False(document.HasDocument);
        Assert.Equal("Error_Unreadable:faux.pdf", Assert.Single(_dialogs.Errors).Message);
    }

    [Fact]
    public async Task Open_MissingFile_ShowsAccessError()
    {
        Assert.False(await _document.OpenFileAsync(Path.Combine(_temp.Path, "absent.pdf")));

        Assert.Equal("Error_FileAccess:absent.pdf", Assert.Single(_dialogs.Errors).Message);
    }

    [Fact]
    public async Task Open_PasswordProtected_AsksUntilRightAndStaysReadOnly()
    {
        _renderer.RequiredPassword = "bon";
        _dialogs.Passwords.Enqueue("faux");
        _dialogs.Passwords.Enqueue("bon");
        var path = Write("secret.pdf", SamplePdf.Create(1, userPassword: "bon", ownerPassword: "proprio"));

        Assert.True(await _document.OpenFileAsync(path));

        Assert.Equal([false, true], _dialogs.PasswordRetries);
        Assert.Equal(PdfEditability.Protected, _document.Editability);
        Assert.False(_document.CanEdit);
        Assert.True(_document.IsReadOnly);
        Assert.Equal("Document_ReadOnlyProtected", _document.ReadOnlyMessage);
        Assert.False(_document.RotateRightCommand.CanExecute(null));
        Assert.True(_document.SaveAsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Open_PasswordCancelled_OpensNothing()
    {
        _renderer.RequiredPassword = "bon";
        var path = Write("secret.pdf", SamplePdf.Create(1, userPassword: "bon", ownerPassword: "proprio"));

        Assert.False(await _document.OpenFileAsync(path));

        Assert.False(_document.HasDocument);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Open_ReadOnly_AnnouncesBannerToTheView()
    {
        var announced = new List<string?>();
        _document.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        await _document.OpenFileAsync(Write("restreint.pdf", SamplePdf.Create(1, ownerPassword: "proprio")));

        // La vue lit IsReadOnly et ReadOnlyMessage à chaque annonce : la dernière doit suivre l'ouverture.
        Assert.True(_document.IsReadOnly);
        var hasDocument = announced.LastIndexOf(nameof(DocumentViewModel.HasDocument));
        Assert.True(announced.LastIndexOf(nameof(DocumentViewModel.IsReadOnly)) > hasDocument);
        Assert.True(announced.LastIndexOf(nameof(DocumentViewModel.ReadOnlyMessage)) > hasDocument);
    }

    [Fact]
    public async Task Open_OwnerRestricted_IsReadOnly()
    {
        await _document.OpenFileAsync(Write("restreint.pdf", SamplePdf.Create(2, ownerPassword: "proprio")));

        Assert.Equal(PdfEditability.Protected, _document.Editability);
        Assert.False(_document.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task RotateRight_ChangesSizeAndMarksModified_UndoRestores()
    {
        await OpenSampleAsync(2);
        var original = _document.Content;
        var portrait = _document.Pages[0].Size;

        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.True(_document.IsModified);
        Assert.Equal("*doc.pdf - Quai PDF", _document.WindowTitle);
        Assert.Equal(portrait.Height, _document.Pages[0].Size.Width, 3);
        Assert.Equal([90, 0], PdfOperations.PageRotations(_document.Content!));
        Assert.True(_document.UndoCommand.CanExecute(null));

        await _document.UndoCommand.ExecuteAsync(null);

        Assert.Same(original, _document.Content);
        Assert.False(_document.IsModified);
        Assert.Equal(portrait, _document.Pages[0].Size);

        await _document.RedoCommand.ExecuteAsync(null);

        Assert.Equal([90, 0], PdfOperations.PageRotations(_document.Content!));
        Assert.True(_document.IsModified);
    }

    [Fact]
    public async Task RotateLeft_AppliesToSelection()
    {
        await OpenSampleAsync(3);
        _document.SetSelection([0, 2]);

        await _document.RotateLeftCommand.ExecuteAsync(null);

        Assert.Equal([270, 0, 270], PdfOperations.PageRotations(_document.Content!));
        Assert.Equal([0, 2], _document.SelectedIndices);
    }

    [Fact]
    public async Task Rotate_WithoutSelection_TargetsVisiblePage()
    {
        await OpenSampleAsync(3);
        _document.GoToPage(2);

        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.Equal([0, 90, 0], PdfOperations.PageRotations(_document.Content!));
    }

    [Fact]
    public async Task Delete_RemovesSelectionAndSelectsFollowingPage()
    {
        await OpenSampleAsync(4);
        _document.SetSelection([1, 2]);

        await _document.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([0, 3], SamplePdf.Identify(_document.Content!));
        Assert.Equal(2, _document.PageCount);
        Assert.Equal([1], _document.SelectedIndices);
    }

    [Fact]
    public async Task Delete_IsDisabledWhenEveryPageIsSelected()
    {
        await OpenSampleAsync(2);

        _document.SetSelection([0, 1]);

        Assert.False(_document.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_LastPage_ClampsCurrentPage()
    {
        await OpenSampleAsync(3);
        _document.GoToPage(3);

        await _document.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, _document.PageCount);
        Assert.Equal(2, _document.CurrentPageNumber);
    }

    [Fact]
    public async Task MoveUpAndDown_FollowSelection()
    {
        await OpenSampleAsync(4);
        _document.SetSelection([2]);

        Assert.True(_document.MoveUpCommand.CanExecute(null));
        await _document.MoveUpCommand.ExecuteAsync(null);

        Assert.Equal([0, 2, 1, 3], SamplePdf.Identify(_document.Content!));
        Assert.Equal([1], _document.SelectedIndices);

        await _document.MoveDownCommand.ExecuteAsync(null);
        await _document.MoveDownCommand.ExecuteAsync(null);

        Assert.Equal([0, 1, 3, 2], SamplePdf.Identify(_document.Content!));
        Assert.Equal([3], _document.SelectedIndices);
        Assert.False(_document.MoveDownCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reorder_FromDragAndDrop_KeepsSelectionOnMovedPages()
    {
        await OpenSampleAsync(4);
        _document.SetSelection([3]);

        await _document.ReorderAsync([3, 0, 1, 2]);

        Assert.Equal([3, 0, 1, 2], SamplePdf.Identify(_document.Content!));
        Assert.Equal([0], _document.SelectedIndices);
    }

    [Fact]
    public async Task Reorder_Failing_PutsThumbnailsBackInContentOrder()
    {
        await OpenSampleAsync(3);
        // Glisser-déposer : la liste des vignettes a déjà déplacé la page 3 en tête.
        _document.Pages.Move(2, 0);

        // Ordre invalide (page 1 en double) : la modification échoue.
        await _document.ReorderAsync([2, 0, 0]);

        Assert.Equal([0, 1, 2], _document.Pages.Select(p => p.Index));
        Assert.Single(_dialogs.Errors);
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task Reorder_Identity_ChangesNothing()
    {
        await OpenSampleAsync(3);
        var loads = _renderer.Loads;

        await _document.ReorderAsync([0, 1, 2]);

        Assert.Equal(loads, _renderer.Loads);
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task Insert_AddsPagesAfterVisiblePageAndSelectsThem()
    {
        await OpenSampleAsync(3);
        _files.InsertPaths.Add(Write("ajout.pdf", SamplePdf.Create(2, firstWidthOffset: 10)));
        _document.GoToPage(1);

        await _document.InsertCommand.ExecuteAsync(null);

        Assert.Equal([0, 10, 11, 1, 2], SamplePdf.Identify(_document.Content!));
        Assert.Equal([1, 2], _document.SelectedIndices);
    }

    [Fact]
    public async Task Insert_ProtectedSource_ShowsErrorWithoutChange()
    {
        await OpenSampleAsync(1);
        _files.InsertPaths.Add(Write("secret.pdf", SamplePdf.Create(1, userPassword: "u", ownerPassword: "o")));

        await _document.InsertCommand.ExecuteAsync(null);

        Assert.Equal("Error_SourceProtected:secret.pdf", Assert.Single(_dialogs.Errors).Message);
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task Extract_WritesSelectedPagesToNewFile()
    {
        await OpenSampleAsync(4);
        _document.SetSelection([1, 3]);
        _files.SavePath = Path.Combine(_temp.Path, "extrait.pdf");

        await _document.ExtractCommand.ExecuteAsync(null);

        Assert.Equal("Document_ExtractFileName:doc", _files.SuggestedName);
        Assert.Equal([1, 3], SamplePdf.Identify(File.ReadAllBytes(_files.SavePath)));
        Assert.False(_document.IsModified);
        Assert.Equal(4, _document.PageCount);
    }

    [Fact]
    public async Task Save_WritesShownContentToFile()
    {
        var path = await OpenSampleAsync(3);
        _document.SetSelection([0]);
        await _document.DeleteCommand.ExecuteAsync(null);

        Assert.True(_document.SaveCommand.CanExecute(null));
        await _document.SaveCommand.ExecuteAsync(null);

        Assert.False(_document.IsModified);
        Assert.Equal([1, 2], SamplePdf.Identify(File.ReadAllBytes(path)));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task UndoAfterSave_MarksModifiedAgain()
    {
        await OpenSampleAsync(2);
        await _document.RotateRightCommand.ExecuteAsync(null);
        await _document.SaveCommand.ExecuteAsync(null);

        await _document.UndoCommand.ExecuteAsync(null);

        Assert.True(_document.IsModified);
    }

    [Fact]
    public async Task SaveAs_WritesNewFileAndFollowsIt()
    {
        var original = await OpenSampleAsync(2);
        await _document.RotateRightCommand.ExecuteAsync(null);
        _files.SavePath = Path.Combine(_temp.Path, "copie.pdf");

        await _document.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal(_files.SavePath, _document.FilePath);
        Assert.Equal([90, 0], PdfOperations.PageRotations(File.ReadAllBytes(_files.SavePath)));
        Assert.Equal([0, 0], PdfOperations.PageRotations(File.ReadAllBytes(original)));
        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task ConfirmClose_CancelKeepsDocument()
    {
        await OpenSampleAsync(2);
        await _document.RotateRightCommand.ExecuteAsync(null);
        _dialogs.SaveChoice = SaveChoice.Cancel;

        Assert.False(await _document.ConfirmCloseAsync());

        Assert.False(await _document.OpenFileAsync(Write("autre.pdf", SamplePdf.Create(1))));
        Assert.Equal("doc.pdf", _document.FileName);
        Assert.Equal(2, _dialogs.SaveQuestions);
    }

    [Fact]
    public async Task ConfirmClose_SaveWritesFile()
    {
        var path = await OpenSampleAsync(2);
        await _document.RotateRightCommand.ExecuteAsync(null);
        _dialogs.SaveChoice = SaveChoice.Save;

        Assert.True(await _document.ConfirmCloseAsync());

        Assert.Equal([90, 0], PdfOperations.PageRotations(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task CloseDocument_DiscardForgetsEverything()
    {
        await OpenSampleAsync(2);
        await _document.RotateRightCommand.ExecuteAsync(null);
        var rendered = _renderer.Created[^1];
        _dialogs.SaveChoice = SaveChoice.Discard;

        await _document.CloseDocumentCommand.ExecuteAsync(null);

        Assert.False(_document.HasDocument);
        Assert.Empty(_document.Pages);
        Assert.Null(_document.Content);
        Assert.False(_document.UndoCommand.CanExecute(null));
        Assert.True(rendered.Disposed);
        Assert.Equal("Quai PDF", _document.WindowTitle);
    }

    [Fact]
    public async Task ReplacedRendering_IsDisposed()
    {
        await OpenSampleAsync(2);
        var first = _renderer.Created[^1];

        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.True(first.Disposed);
        Assert.False(_renderer.Created[^1].Disposed);
    }

    [Fact]
    public async Task EditProperties_WritesMetadata()
    {
        await OpenSampleAsync(1);
        _dialogs.EditMetadata = editor =>
        {
            editor.Title = "  Nouveau titre ";
            editor.Author = "Alice";
            return true;
        };

        await _document.EditPropertiesCommand.ExecuteAsync(null);

        Assert.Equal(new PdfMetadata("Nouveau titre", "Alice", string.Empty, string.Empty), PdfOperations.ReadMetadata(_document.Content!));
        Assert.True(_document.IsModified);
    }

    [Fact]
    public async Task EditProperties_CancelledChangesNothing()
    {
        await OpenSampleAsync(1);
        _dialogs.EditMetadata = _ => false;

        await _document.EditPropertiesCommand.ExecuteAsync(null);

        Assert.False(_document.IsModified);
    }

    [Fact]
    public async Task Zoom_StepsAndClamps()
    {
        await OpenSampleAsync(1);
        _document.Zoom = 1;

        _document.ZoomInCommand.Execute(null);
        Assert.Equal(1.1, _document.Zoom, 3);
        Assert.Equal("110 %", _document.ZoomText);
        Assert.Equal(_document.Pages[0].Size.Width * 1.1, _document.Pages[0].DisplayWidth, 3);

        _document.ZoomOutCommand.Execute(null);
        _document.ZoomOutCommand.Execute(null);
        Assert.Equal(0.9, _document.Zoom, 3);

        _document.Zoom = 50;
        Assert.Equal(DocumentViewModel.MaxZoom, _document.Zoom);
        _document.ZoomInCommand.Execute(null);
        Assert.Equal(DocumentViewModel.MaxZoom, _document.Zoom);

        _document.Zoom = 0;
        Assert.Equal(DocumentViewModel.MinZoom, _document.Zoom);
    }

    [Fact]
    public async Task FitWidth_UsesWidestPage()
    {
        await OpenSampleAsync(3);
        var widest = _document.Pages.Max(p => p.Size.Width);
        _document.ViewportWidth = 48 + (widest * 2);

        _document.FitWidthCommand.Execute(null);

        Assert.Equal(2, _document.Zoom, 3);
    }

    [Fact]
    public async Task Navigation_RaisesScrollAndUpdatesCommands()
    {
        await OpenSampleAsync(3);
        var scrolled = new List<int>();
        _document.ScrollRequested += (_, index) => scrolled.Add(index);

        Assert.False(_document.PreviousPageCommand.CanExecute(null));
        _document.NextPageCommand.Execute(null);
        _document.GoToPage(99);

        Assert.Equal([1, 2], scrolled);
        Assert.Equal(3, _document.CurrentPageNumber);
        Assert.False(_document.NextPageCommand.CanExecute(null));
        Assert.True(_document.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task ContentChanged_CarriesSelectionAndScroll()
    {
        await OpenSampleAsync(3);
        DocumentChangedEventArgs? change = null;
        _document.ContentChanged += (_, e) => change = e;
        _document.SetSelection([2]);

        await _document.MoveUpCommand.ExecuteAsync(null);

        Assert.NotNull(change);
        Assert.Equal([1], change.Selection);
        Assert.Equal(1, change.ScrollTo);
    }

    [Fact]
    public async Task FailingEdit_ShowsErrorAndKeepsContent()
    {
        await OpenSampleAsync(2);
        var before = _document.Content;
        _files.InsertPaths.Add(Write("faux.pdf", "pas un pdf"u8.ToArray()));

        await _document.InsertCommand.ExecuteAsync(null);

        Assert.Same(before, _document.Content);
        Assert.Equal("Error_Unreadable:faux.pdf", Assert.Single(_dialogs.Errors).Message);
        Assert.False(_document.UndoCommand.CanExecute(null));
    }

    private async Task<string> OpenSampleAsync(int pages)
    {
        var path = Write("doc.pdf", SamplePdf.Create(pages));
        Assert.True(await _document.OpenFileAsync(path));
        return path;
    }

    private string Write(string name, byte[] content)
    {
        var path = _temp.File(name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private sealed class ThrowingRenderer : IPdfRenderer
    {
        public Task<IRenderedPdf> LoadAsync(byte[] content, string? password, CancellationToken cancellationToken = default) =>
            throw new PdfFileException(PdfProblem.Unreadable, "Illisible.");
    }
}
