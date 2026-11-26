using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace QuaiPdf.App.Views;

/// <summary>
/// Surface d'une page : curseur de texte (barre verticale), comme sur une page web ; flèche de déplacement au-dessus
/// d'une zone de texte qui peut bouger, flèche simple au-dessus d'une zone figée.
/// </summary>
public sealed partial class TextPageSurface : Grid
{
    private InputSystemCursorShape _shape = InputSystemCursorShape.IBeam;

    public TextPageSurface()
    {
        ProtectedCursor = InputSystemCursor.Create(_shape);
    }

    /// <summary>Change le curseur au-dessus de la page (rien ne se passe s'il est déjà celui-là).</summary>
    public void ShowCursor(InputSystemCursorShape shape)
    {
        if (shape != _shape)
        {
            _shape = shape;
            ProtectedCursor = InputSystemCursor.Create(shape);
        }
    }
}
