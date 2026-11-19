namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Taille d'une page telle qu'affichée (rotation comprise), en pixels indépendants (1/96 de pouce) à 100 %.
/// </summary>
public readonly record struct PdfPageSize(double Width, double Height)
{
    /// <summary>Points PDF (1/72 de pouce) vers pixels indépendants (1/96 de pouce).</summary>
    public const double PixelsPerPoint = 96.0 / 72.0;

    public static PdfPageSize FromPoints(double width, double height) => new(width * PixelsPerPoint, height * PixelsPerPoint);
}
