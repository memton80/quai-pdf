namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Historique « annuler / rétablir » : les contenus successifs du document, gardés en mémoire.
/// Borné à <see cref="Capacity"/> étapes (les plus anciennes sont oubliées).
/// </summary>
public sealed class PdfHistory
{
    public const int Capacity = 30;

    private readonly LinkedList<byte[]> _undo = new();
    private readonly Stack<byte[]> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Contenu que rétablirait « Annuler », ou <c>null</c>.</summary>
    public byte[]? UndoTarget => _undo.Last?.Value;

    /// <summary>Contenu que rétablirait « Rétablir », ou <c>null</c>.</summary>
    public byte[]? RedoTarget => _redo.TryPeek(out var next) ? next : null;

    /// <summary>Une modification vient d'être faite : <paramref name="previous"/> est le contenu d'avant.</summary>
    public void Record(byte[] previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        _undo.AddLast(previous);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    /// <summary>« Annuler » a été appliqué : <see cref="UndoTarget"/> est affiché, <paramref name="current"/> part dans « Rétablir ».</summary>
    public void Undone(byte[] current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("Rien à annuler.");
        }

        _undo.RemoveLast();
        _redo.Push(current);
    }

    /// <summary>« Rétablir » a été appliqué : <see cref="RedoTarget"/> est affiché, <paramref name="current"/> repart dans « Annuler ».</summary>
    public void Redone(byte[] current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!_redo.TryPop(out _))
        {
            throw new InvalidOperationException("Rien à rétablir.");
        }

        _undo.AddLast(current);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
