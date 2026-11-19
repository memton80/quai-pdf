using CommunityToolkit.Mvvm.ComponentModel;
using QuaiPdf.Core.Pdf;

namespace QuaiPdf.Core.ViewModels;

/// <summary>Fiche « Propriétés du document » : titre, auteur, sujet, mots-clés.</summary>
public sealed partial class MetadataEditorViewModel : ObservableObject
{
    public MetadataEditorViewModel(PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Title = metadata.Title;
        Author = metadata.Author;
        Subject = metadata.Subject;
        Keywords = metadata.Keywords;
    }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Author { get; set; }

    [ObservableProperty]
    public partial string Subject { get; set; }

    [ObservableProperty]
    public partial string Keywords { get; set; }

    public PdfMetadata Result => new(Title.Trim(), Author.Trim(), Subject.Trim(), Keywords.Trim());
}
