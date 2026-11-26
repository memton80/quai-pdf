using System.Runtime.InteropServices;
using System.Text;
using static QuaiPdf.Core.Pdf.Text.PdfiumNative;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>Une entrée du plan du document (signet) : son titre, sa page (-1 si elle n'en désigne aucune), ses enfants.</summary>
public sealed record PdfOutlineItem(string Title, int PageIndex, IReadOnlyList<PdfOutlineItem> Children);

public sealed partial class PdfTextDocument
{
    // Un plan abîmé ou malveillant peut boucler ou être immense : la lecture s'arrête à ces limites.
    internal const int MaxOutlineItems = 5000;
    internal const int MaxOutlineDepth = 32;

    /// <summary>Plan du document (signets), lu par PDFium ; liste vide si le document n'en a pas.</summary>
    public IReadOnlyList<PdfOutlineItem> Outline()
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var seen = new HashSet<IntPtr>();
            return ReadChildren(IntPtr.Zero, 0, seen);
        }
    }

    private List<PdfOutlineItem> ReadChildren(IntPtr parent, int depth, HashSet<IntPtr> seen)
    {
        var items = new List<PdfOutlineItem>();
        if (depth >= MaxOutlineDepth)
        {
            return items;
        }

        for (var bookmark = FPDFBookmark_GetFirstChild(_document, parent);
             bookmark != IntPtr.Zero && seen.Count < MaxOutlineItems && seen.Add(bookmark);
             bookmark = FPDFBookmark_GetNextSibling(_document, bookmark))
        {
            var children = ReadChildren(bookmark, depth + 1, seen);
            items.Add(new PdfOutlineItem(Title(bookmark), PageOf(bookmark), children));
        }

        return items;
    }

    private static string Title(IntPtr bookmark)
    {
        var size = (int)Math.Min(FPDFBookmark_GetTitle(bookmark, null, new CULong(0)).Value, 64 * 1024);
        if (size <= 2)
        {
            return string.Empty;
        }

        var buffer = new byte[size];
        FPDFBookmark_GetTitle(bookmark, buffer, new CULong((uint)size));
        var title = Encoding.Unicode.GetString(buffer, 0, size - 2);

        // Les titres contiennent parfois des fins de ligne ou des tabulations : une seule ligne à l'écran.
        return string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Page désignée par un signet : sa destination, ou celle de son action « aller à ».</summary>
    private int PageOf(IntPtr bookmark)
    {
        var destination = FPDFBookmark_GetDest(_document, bookmark);
        if (destination == IntPtr.Zero)
        {
            var action = FPDFBookmark_GetAction(bookmark);
            if (action != IntPtr.Zero && FPDFAction_GetType(action).Value == ActionGoTo)
            {
                destination = FPDFAction_GetDest(_document, action);
            }
        }

        if (destination == IntPtr.Zero)
        {
            return -1;
        }

        var page = FPDFDest_GetDestPageIndex(_document, destination);
        return page >= 0 && page < PageCount ? page : -1;
    }
}
