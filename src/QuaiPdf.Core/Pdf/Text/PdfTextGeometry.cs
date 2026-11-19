namespace QuaiPdf.Core.Pdf.Text;

/// <summary>Point d'une page affichée : pixels indépendants à 100 %, origine en haut à gauche, rotation comprise.</summary>
public readonly record struct PdfPoint(double X, double Y);

/// <summary>Rectangle d'une page affichée : mêmes unités et même repère que <see cref="PdfPoint"/>.</summary>
public readonly record struct PdfRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(PdfPoint point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
}

/// <summary>
/// Une place dans le texte du document : page, et rang du caractère devant lequel on se trouve (de 0 à
/// <see cref="PdfTextPage.CharCount"/>, comme le curseur d'un éditeur de texte).
/// </summary>
public readonly record struct PdfTextPosition(int Page, int Char) : IComparable<PdfTextPosition>
{
    public int CompareTo(PdfTextPosition other) => Page != other.Page ? Page.CompareTo(other.Page) : Char.CompareTo(other.Char);
}

/// <summary>
/// Texte sélectionné, de la place où la sélection a commencé (<see cref="Anchor"/>) à celle où elle s'arrête
/// (<see cref="Focus"/>), dans un sens ou dans l'autre, éventuellement sur plusieurs pages.
/// Vide tant que les deux places sont confondues (simple clic).
/// </summary>
public sealed record PdfTextSelection(PdfTextPosition Anchor, PdfTextPosition Focus)
{
    public bool IsEmpty => Anchor == Focus;

    public PdfTextPosition Start => Anchor.CompareTo(Focus) <= 0 ? Anchor : Focus;

    public PdfTextPosition End => Anchor.CompareTo(Focus) <= 0 ? Focus : Anchor;

    /// <summary>Caractères sélectionnés sur une page (bornes comprises), ou <c>null</c> si aucun.</summary>
    public (int From, int To)? RangeOn(int page, int charCount)
    {
        if (IsEmpty || charCount <= 0 || page < Start.Page || page > End.Page)
        {
            return null;
        }

        var from = Math.Clamp(page == Start.Page ? Start.Char : 0, 0, charCount);
        var end = Math.Clamp(page == End.Page ? End.Char : charCount, 0, charCount);
        return from < end ? (from, end - 1) : null;
    }
}
