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
    private readonly FakeClipboard _clipboard = new();
    private readonly DocumentViewModel _document;

    public DocumentViewModelTests()
    {
        _document = new DocumentViewModel(_renderer, _dialogs, _files, new FakeStrings(), _clipboard);
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
    public async Task Open_UnreadableFile_ShowsErrorAndKeepsNothing()
    {
        var path = Write("faux.pdf", "pas un pdf"u8.ToArray());
        var renderer = new ThrowingRenderer();
        using var document = new DocumentViewModel(renderer, _dialogs, _files, new FakeStrings(), _clipboard);

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
    public async Task SaveOrCopy_SavesChangesInPlace()
    {
        var path = await OpenSampleAsync(2);
        Assert.True(_document.SaveOrCopyCommand.CanExecute(null));
        await _document.RotateRightCommand.ExecuteAsync(null);

        await _document.SaveOrCopyCommand.ExecuteAsync(null);

        Assert.False(_document.IsModified);
        Assert.Equal(path, _document.FilePath);
        Assert.Equal([90, 0], PdfOperations.PageRotations(File.ReadAllBytes(path)));
        Assert.Null(_files.SuggestedName);
    }

    [Fact]
    public async Task SaveOrCopy_WithoutChanges_WritesNothing()
    {
        await OpenSampleAsync(1);
        _files.SavePath = Path.Combine(_temp.Path, "copie.pdf");

        await _document.SaveOrCopyCommand.ExecuteAsync(null);

        Assert.Null(_files.SuggestedName);
        Assert.False(File.Exists(_files.SavePath));
    }

    [Fact]
    public async Task SaveOrCopy_ReadOnly_SavesACopy()
    {
        await _document.OpenFileAsync(Write("restreint.pdf", SamplePdf.Create(1, ownerPassword: "proprio")));
        _files.SavePath = Path.Combine(_temp.Path, "copie.pdf");

        await _document.SaveOrCopyCommand.ExecuteAsync(null);

        Assert.Equal("restreint.pdf", _files.SuggestedName);
        Assert.True(File.Exists(_files.SavePath));
    }

    [Fact]
    public void SaveOrCopy_IsDisabledWithoutDocument()
    {
        Assert.False(_document.SaveOrCopyCommand.CanExecute(null));
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

        Assert.True(await _document.CloseAsync());

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
    public async Task Open_BeforeTheViewHasAWidth_FitsWidthOnceTheWidthIsKnown()
    {
        // Première ouverture : la zone de lecture, masquée sans document, n'a pas encore de taille.
        await OpenSampleAsync(1);
        Assert.Equal(1, _document.Zoom, 3);

        _document.ViewportWidth = 48 + (_document.Pages[0].Size.Width * 0.5);
        Assert.Equal(0.5, _document.Zoom, 3);

        // Seulement à l'ouverture : redimensionner la fenêtre ensuite ne touche plus au zoom.
        _document.ViewportWidth = 48 + _document.Pages[0].Size.Width;
        Assert.Equal(0.5, _document.Zoom, 3);
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
    public async Task Open_SelectsTheFirstPage()
    {
        DocumentChangedEventArgs? change = null;
        _document.ContentChanged += (_, e) => change = e;

        await OpenSampleAsync(3);

        Assert.Equal([0], _document.SelectedIndices);
        Assert.NotNull(change);
        Assert.Equal([0], change.Selection);
    }

    [Fact]
    public async Task Scrolling_MovesTheSinglePageSelectionWithTheVisiblePage()
    {
        await OpenSampleAsync(4);
        var followed = 0;
        _document.SelectionFollowedPage += (_, _) => followed++;

        // La vue renseigne la page visible au défilement ; les boutons passent par GoToPage.
        _document.CurrentPageNumber = 3;
        Assert.Equal([2], _document.SelectedIndices);

        _document.NextPageCommand.Execute(null);
        Assert.Equal([3], _document.SelectedIndices);
        Assert.Equal(2, followed);

        // Vignette cliquée : la sélection est déjà la page montrée, rien à reporter.
        _document.SetSelection([0]);
        _document.GoToPage(1);
        Assert.Equal(2, followed);
    }

    [Fact]
    public async Task Scrolling_KeepsASelectionOfSeveralPages()
    {
        await OpenSampleAsync(4);
        _document.SetSelection([0, 2]);
        var followed = 0;
        _document.SelectionFollowedPage += (_, _) => followed++;

        _document.CurrentPageNumber = 4;

        Assert.Equal([0, 2], _document.SelectedIndices);
        Assert.Equal(0, followed);
    }

    [Fact]
    public async Task Scrolling_AfterClearingTheSelection_SelectsTheVisiblePageAgain()
    {
        await OpenSampleAsync(3);
        _document.SetSelection([]);

        _document.CurrentPageNumber = 2;

        Assert.Equal([1], _document.SelectedIndices);
    }

    [Fact]
    public async Task Undo_SelectsTheVisiblePage()
    {
        await OpenSampleAsync(3);
        _document.SetSelection([0, 1]);
        await _document.RotateRightCommand.ExecuteAsync(null);
        DocumentChangedEventArgs? change = null;
        _document.ContentChanged += (_, e) => change = e;

        await _document.UndoCommand.ExecuteAsync(null);

        Assert.Equal([0], _document.SelectedIndices);
        Assert.NotNull(change);
        Assert.Equal([0], change.Selection);
    }

    [Fact]
    public async Task Reorder_OnePage_ShowsItWhereItLanded()
    {
        await OpenSampleAsync(4);
        _document.GoToPage(2);
        DocumentChangedEventArgs? change = null;
        _document.ContentChanged += (_, e) => change = e;

        // Glisser-déposer de la vignette 4 en tête.
        _document.SetSelection([3]);
        await _document.ReorderAsync([3, 0, 1, 2]);

        Assert.NotNull(change);
        Assert.Equal(0, change.ScrollTo);
        Assert.Equal([0], change.Selection);
        Assert.Equal(1, _document.CurrentPageNumber);
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

    // ----- Texte -----

    [Fact]
    public async Task DragOverText_SelectsAndCopies()
    {
        await OpenTextAsync();

        Assert.True(_document.BeginTextSelection(0, SamplePdf.TextPoint(76, 708), extend: false));
        _document.ExtendTextSelection(0, SamplePdf.TextPoint(250, 708));

        Assert.StartsWith("Bonjour le", _document.SelectedText);
        Assert.NotEmpty(_document.TextSelectionRects(0));
        Assert.True(_document.CopyTextCommand.CanExecute(null));
        await _document.CopyTextCommand.ExecuteAsync(null);
        Assert.Equal(_document.SelectedText, _clipboard.Text);
    }

    [Fact]
    public async Task DragBackwards_SelectsTheSameText()
    {
        await OpenTextAsync();

        _document.BeginTextSelection(0, SamplePdf.TextPoint(560, 708), extend: false);
        _document.ExtendTextSelection(0, SamplePdf.TextPoint(50, 708));

        Assert.Equal("Bonjour le monde", _document.SelectedText);
    }

    [Fact]
    public async Task Click_WithoutDragging_ClearsSelection()
    {
        await OpenTextAsync();
        _document.SelectWordAt(0, SamplePdf.TextPoint(78, 708));
        var changes = 0;
        _document.TextSelectionChanged += (_, _) => changes++;

        Assert.True(_document.BeginTextSelection(0, SamplePdf.TextPoint(140, 708), extend: false));

        Assert.False(_document.HasTextSelection);
        Assert.Empty(_document.TextSelectionRects(0));
        Assert.Equal(string.Empty, _document.SelectedText);
        Assert.Equal(1, changes);
        Assert.False(_document.CopyTextCommand.CanExecute(null));
    }

    [Fact]
    public async Task DragFromMargin_SelectsFromLineStart()
    {
        await OpenTextAsync();

        _document.BeginTextSelection(0, SamplePdf.TextPoint(20, 708), extend: false);
        _document.ExtendTextSelection(0, SamplePdf.TextPoint(560, 708));

        Assert.Equal("Bonjour le monde", _document.SelectedText);
    }

    [Fact]
    public async Task ShiftPress_ExtendsSelection()
    {
        await OpenTextAsync();
        _document.BeginTextSelection(0, SamplePdf.TextPoint(76, 708), extend: false);

        _document.BeginTextSelection(0, SamplePdf.TextPoint(560, 678), extend: true);

        Assert.Equal("Bonjour le monde" + Environment.NewLine + "Deuxieme ligne", _document.SelectedText.ReplaceLineEndings());
    }

    [Fact]
    public async Task DoubleClick_SelectsWord()
    {
        await OpenTextAsync();

        _document.SelectWordAt(0, SamplePdf.TextPoint(140, 708));

        Assert.Equal("Bonjour", _document.SelectedText);
    }

    [Fact]
    public async Task SelectAll_CoversEveryPage()
    {
        await OpenTextAsync();

        _document.SelectAllTextCommand.Execute(null);

        var lines = _document.SelectedText.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(["Bonjour le monde", "Deuxieme ligne", "Page deux"], lines);
        Assert.NotEmpty(_document.TextSelectionRects(1));
    }

    [Fact]
    public async Task SelectionAcrossPages_TakesEndOfFirstAndStartOfSecond()
    {
        await OpenTextAsync();

        _document.BeginTextSelection(0, SamplePdf.TextPoint(76, 678), extend: false);
        _document.ExtendTextSelection(1, SamplePdf.TextPoint(98, 708));

        Assert.Equal("Deuxieme ligne" + Environment.NewLine + "Pa", _document.SelectedText.ReplaceLineEndings());
    }

    [Fact]
    public async Task Edit_ClearsSelection()
    {
        await OpenTextAsync();
        _document.SelectAllTextCommand.Execute(null);

        await _document.RotateRightCommand.ExecuteAsync(null);

        Assert.False(_document.HasTextSelection);
        _document.SelectWordAt(0, new QuaiPdf.Core.Pdf.Text.PdfPoint(708 * 4.0 / 3, 140 * 4.0 / 3));
        Assert.Equal("Bonjour", _document.SelectedText);
    }

    [Fact]
    public async Task PageWithoutText_SelectsNothing()
    {
        await OpenSampleAsync(1);

        Assert.False(_document.BeginTextSelection(0, new QuaiPdf.Core.Pdf.Text.PdfPoint(20, 20), extend: false));
        _document.SelectAllTextCommand.Execute(null);

        Assert.False(_document.HasTextSelection);
    }

    [Fact]
    public async Task CopyForbiddenByAuthor_ShowsMessage()
    {
        await _document.OpenFileAsync(Write("interdit.pdf", SamplePdf.CreateWithText(ownerPassword: "proprio", permitCopy: false, pages: [["Secret"]])));
        _document.SelectAllTextCommand.Execute(null);

        await _document.CopyTextCommand.ExecuteAsync(null);

        Assert.Null(_clipboard.Text);
        Assert.Equal("Error_CopyForbidden", Assert.Single(_dialogs.Errors).Message);
    }

    [Fact]
    public async Task ClipboardBusy_ShowsMessage()
    {
        await OpenTextAsync();
        _document.SelectAllTextCommand.Execute(null);
        _clipboard.Fail = true;

        await _document.CopyTextCommand.ExecuteAsync(null);

        Assert.Equal("Error_Clipboard", Assert.Single(_dialogs.Errors).Message);
    }

    [Fact]
    public async Task PasswordProtectedDocument_TextUsesThePassword()
    {
        _renderer.RequiredPassword = "bon";
        _dialogs.Passwords.Enqueue("bon");
        await _document.OpenFileAsync(Write("secret.pdf", SamplePdf.Create(1, userPassword: "bon", ownerPassword: "proprio")));

        Assert.NotNull(_document.TextPage(0));
    }

    [Fact]
    public async Task CloseDocument_ForgetsSelection()
    {
        await OpenTextAsync();
        _document.SelectAllTextCommand.Execute(null);

        Assert.True(await _document.CloseAsync());

        Assert.False(_document.HasTextSelection);
        Assert.Null(_document.TextPage(0));
    }

    [Fact]
    public async Task StatusText_FollowsTheDocumentState()
    {
        Assert.Equal(string.Empty, _document.StatusText);

        await OpenSampleAsync(2);
        Assert.Equal("Status_Saved", _document.StatusText);

        await _document.RotateRightCommand.ExecuteAsync(null);
        Assert.Equal("Status_Modified", _document.StatusText);

        _dialogs.SaveChoice = SaveChoice.Discard;
        Assert.True(await _document.OpenFileAsync(Write("protege.pdf", SamplePdf.Create(1, ownerPassword: "proprio"))));
        Assert.Equal("Status_ReadOnly", _document.StatusText);
    }

    [Fact]
    public async Task TabTitle_IsTheFileNameWithItsPathAsToolTip()
    {
        Assert.Equal("Tab_Home", _document.TabTitle);

        var path = await OpenSampleAsync(1);

        Assert.Equal("doc.pdf", _document.TabTitle);
        Assert.Equal(path, _document.TabToolTip);
    }

    private Task<bool> OpenTextAsync() =>
        _document.OpenFileAsync(Write("texte.pdf", SamplePdf.CreateWithText(pages: [["Bonjour le monde", "Deuxieme ligne"], ["Page deux"]])));

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
