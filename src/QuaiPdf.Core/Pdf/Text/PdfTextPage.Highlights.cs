using static QuaiPdf.Core.Pdf.Text.PdfiumNative;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>Surlignages (annotations <c>/Highlight</c>) de la page, lus par PDFium.</summary>
public sealed partial class PdfTextPage
{
    // Un document piégé pourrait annoncer des millions de quadrilatères.
    private const int MaxQuads = 10_000;

    /// <summary>
    /// Surlignages visibles de la page, avec leurs rectangles affichés (même repère que la sélection de texte) et ce que
    /// leur auteur permet.
    /// </summary>
    public IReadOnlyList<PdfHighlight> Highlights()
    {
        lock (Gate)
        {
            if (_owner.IsDisposed || _page == IntPtr.Zero)
            {
                return [];
            }

            var highlights = new List<PdfHighlight>();
            var count = FPDFPage_GetAnnotCount(_page);
            for (var index = 0; index < count; index++)
            {
                var annotation = FPDFPage_GetAnnot(_page, index);
                if (annotation == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (ReadHighlight(annotation, index) is { } highlight)
                    {
                        highlights.Add(highlight);
                    }
                }
                finally
                {
                    FPDFPage_CloseAnnot(annotation);
                }
            }

            return highlights;
        }
    }

    private PdfHighlight? ReadHighlight(IntPtr annotation, int index)
    {
        if (FPDFAnnot_GetSubtype(annotation) != AnnotHighlight)
        {
            return null;
        }

        var flags = FPDFAnnot_GetFlags(annotation);
        if ((flags & (PdfTextBoxes.FlagHidden | PdfTextBoxes.FlagNoView)) != 0)
        {
            return null;
        }

        var areas = new List<PdfRect>();
        var quads = (int)Math.Min(FPDFAnnot_CountAttachmentPoints(annotation), MaxQuads);
        for (var i = 0; i < quads; i++)
        {
            if (FPDFAnnot_GetAttachmentPoints(annotation, (nuint)i, out var q)
                && Area([(q.X1, q.Y1), (q.X2, q.Y2), (q.X3, q.Y3), (q.X4, q.Y4)]) is { } area)
            {
                areas.Add(area);
            }
        }

        // Sans quadrilatères lisibles : le rectangle de l'annotation.
        if (areas.Count == 0 && FPDFAnnot_GetRect(annotation, out var rect)
            && Area([(rect.Left, rect.Top), (rect.Right, rect.Bottom)]) is { } whole)
        {
            areas.Add(whole);
        }

        return areas.Count == 0
            ? null
            : new PdfHighlight(Index, index, areas, CanEdit: (flags & (PdfTextBoxes.FlagReadOnly | PdfTextBoxes.FlagLocked)) == 0);
    }

    /// <summary>Rectangle affiché qui englobe ces points du PDF, ou <c>null</c>.</summary>
    private PdfRect? Area(IReadOnlyList<(float X, float Y)> points)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var (px, py) in points)
        {
            if (!ToDevice(px, py, out var x, out var y))
            {
                return null;
            }

            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        return right > left && bottom > top ? new PdfRect(left, top, right - left, bottom - top) : null;
    }
}
