using QuaiPdf.Core.Localization;
using QuaiPdf.Core.Pdf;
using QuaiPdf.Core.Services;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.Core.Tests.Support;

/// <summary>Chaînes de test : la clé, suivie des arguments.</summary>
public sealed class FakeStrings : IStrings
{
    public string Get(string key) => key;

    public string Format(string key, params object?[] args) => args.Length == 0 ? key : $"{key}:{string.Join("|", args)}";
}

/// <summary>
/// Moteur d'affichage de test : tailles lues avec PDFsharp. <see cref="RequiredPassword"/> simule un document chiffré
/// (le moteur de Windows demande alors un mot de passe).
/// </summary>
public sealed class FakeRenderer : IPdfRenderer
{
    public string? RequiredPassword { get; set; }

    public int Loads { get; private set; }

    public List<FakeRendered> Created { get; } = [];

    public Task<IRenderedPdf> LoadAsync(byte[] content, string? password, CancellationToken cancellationToken = default)
    {
        Loads++;
        if (RequiredPassword is not null && password != RequiredPassword)
        {
            throw new PdfPasswordException("Mot de passe requis.");
        }

        IReadOnlyList<PdfPageSize> sizes;
        try
        {
            sizes = PdfOperations.PageSizes(content);
        }
        catch (PdfFileException) when (RequiredPassword is not null)
        {
            // PDFsharp ne lit pas sans mot de passe : une page de taille fixe suffit au test.
            sizes = [new PdfPageSize(100, 200)];
        }

        var rendered = new FakeRendered(sizes);
        Created.Add(rendered);
        return Task.FromResult<IRenderedPdf>(rendered);
    }
}

public sealed class FakeRendered(IReadOnlyList<PdfPageSize> pages) : IRenderedPdf
{
    public IReadOnlyList<PdfPageSize> Pages { get; } = pages;

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Passwords { get; } = new();

    public List<bool> PasswordRetries { get; } = [];

    public SaveChoice SaveChoice { get; set; } = SaveChoice.Cancel;

    public int SaveQuestions { get; private set; }

    public List<(string Title, string Message)> Errors { get; } = [];

    public Func<MetadataEditorViewModel, bool>? EditMetadata { get; set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false) => Task.FromResult(true);

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<string?> PromptPasswordAsync(string fileName, bool isRetry)
    {
        PasswordRetries.Add(isRetry);
        return Task.FromResult(Passwords.Count > 0 ? Passwords.Dequeue() : null);
    }

    public Task<SaveChoice> AskSaveChangesAsync(string fileName)
    {
        SaveQuestions++;
        return Task.FromResult(SaveChoice);
    }

    public Task<bool> EditMetadataAsync(MetadataEditorViewModel editor) => Task.FromResult(EditMetadata?.Invoke(editor) ?? false);
}

public sealed class FakeFileDialogs : IFileDialogs
{
    public string? OpenPath { get; set; }

    public List<string> InsertPaths { get; } = [];

    public string? SavePath { get; set; }

    public string? SuggestedName { get; private set; }

    public Task<string?> PickPdfToOpenAsync() => Task.FromResult(OpenPath);

    public Task<IReadOnlyList<string>> PickPdfsToInsertAsync() => Task.FromResult<IReadOnlyList<string>>(InsertPaths.ToList());

    public Task<string?> PickSaveLocationAsync(string suggestedFileName, string? folder)
    {
        SuggestedName = suggestedFileName;
        return Task.FromResult(SavePath);
    }
}

public sealed class FakeClipboard : IClipboard
{
    public string? Text { get; private set; }

    public bool Fail { get; set; }

    public void SetText(string text)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Presse-papiers occupé.");
        }

        Text = text;
    }
}

/// <summary>Chaînes de test (format de Quai) : la clé, et les arguments entre parenthèses.</summary>
public sealed class KeyStrings : IStrings
{
    public string Get(string key) => key;

    public string Format(string key, params object?[] args) => $"{key}({string.Join(",", args)})";
}

/// <summary>Installation scriptée d'une mise à jour : enregistre ce qui serait lancé (repris de Quai).</summary>
public sealed class FakeUpdateInstaller : QuaiPdf.Core.Updates.IUpdateInstaller
{
    public bool CanInstall { get; set; } = true;

    /// <summary>Réponse à la question (document modifié) : faux = l'utilisateur garde son document ouvert.</summary>
    public bool Accept { get; set; } = true;

    public Exception? Failure { get; set; }

    public List<string> Installed { get; } = [];

    public List<Uri> OpenedPages { get; } = [];

    public Task<bool> InstallAndRestartAsync(string installerPath)
    {
        if (Failure is not null)
        {
            return Task.FromException<bool>(Failure);
        }

        if (Accept)
        {
            Installed.Add(installerPath);
        }

        return Task.FromResult(Accept);
    }

    public void OpenReleasePage(Uri page) => OpenedPages.Add(page);
}
