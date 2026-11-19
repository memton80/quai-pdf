namespace QuaiPdf.Core.Pdf;

/// <summary>Propriétés du document (dictionnaire <c>/Info</c>) modifiables dans Quai PDF.</summary>
public sealed record PdfMetadata(string Title, string Author, string Subject, string Keywords)
{
    public static PdfMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}
