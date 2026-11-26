using Microsoft.Windows.Storage.Pickers;
using QuaiPdf.App.Localization;
using QuaiPdf.Core.Services;

namespace QuaiPdf.App.Services;

/// <summary>Sélecteurs de fichiers de Windows (API du Windows App SDK, utilisable sans paquet MSIX).</summary>
internal sealed class FileDialogs : IFileDialogs
{
    /// <summary>Identifiant de la fenêtre principale ; renseigné par celle-ci à sa création.</summary>
    public Microsoft.UI.WindowId WindowId { get; set; }

    public Task<IReadOnlyList<string>> PickPdfsToOpenAsync() => PickPdfsAsync();

    public Task<IReadOnlyList<string>> PickPdfsToInsertAsync() => PickPdfsAsync();

    private async Task<IReadOnlyList<string>> PickPdfsAsync()
    {
        var picker = new FileOpenPicker(WindowId) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".pdf");
        var results = await picker.PickMultipleFilesAsync();
        return results is null ? [] : results.Select(r => r.Path).ToList();
    }

    public async Task<string?> PickSaveLocationAsync(string suggestedFileName, string? folder)
    {
        var picker = new FileSavePicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
        };
        picker.FileTypeChoices.Add(Loc.Get("Picker_PdfDocument"), [".pdf"]);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            picker.SuggestedFolder = folder;
        }

        var result = await picker.PickSaveFileAsync();
        if (result is null)
        {
            return null;
        }

        return string.Equals(Path.GetExtension(result.Path), ".pdf", StringComparison.OrdinalIgnoreCase)
            ? result.Path
            : result.Path + ".pdf";
    }
}
