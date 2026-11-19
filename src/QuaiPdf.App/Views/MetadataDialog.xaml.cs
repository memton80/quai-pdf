using Microsoft.UI.Xaml.Controls;
using QuaiPdf.Core.ViewModels;

namespace QuaiPdf.App.Views;

/// <summary>Propriétés du document : titre, auteur, sujet, mots-clés.</summary>
public sealed partial class MetadataDialog : ContentDialog
{
    public MetadataDialog(MetadataEditorViewModel editor)
    {
        Editor = editor;
        InitializeComponent();
    }

    public MetadataEditorViewModel Editor { get; }
}
