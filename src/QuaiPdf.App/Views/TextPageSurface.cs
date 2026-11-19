using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace QuaiPdf.App.Views;

/// <summary>Surface d'une page : curseur de texte (barre verticale), comme sur une page web.</summary>
public sealed partial class TextPageSurface : Grid
{
    public TextPageSurface()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
    }
}
