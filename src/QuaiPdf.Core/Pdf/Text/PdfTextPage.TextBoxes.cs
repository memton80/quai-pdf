using System.Runtime.InteropServices;
using System.Text;
using static QuaiPdf.Core.Pdf.Text.PdfiumNative;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>Zones de texte (annotations <c>/FreeText</c>) de la page, lues par PDFium, et rendu de contrôle.</summary>
public sealed partial class PdfTextPage
{
    /// <summary>
    /// Zones de texte visibles de la page, avec leur rectangle affiché (même repère que la sélection de texte), leur
    /// texte, leur taille, leur couleur et ce que leur auteur permet (une zone avec une ligne de rappel n'est ni modifiée
    /// ni déplacée ici : son apparence la dessine).
    /// </summary>
    public IReadOnlyList<PdfTextBox> TextBoxes()
    {
        lock (Gate)
        {
            if (_owner.IsDisposed || _page == IntPtr.Zero)
            {
                return [];
            }

            var boxes = new List<PdfTextBox>();
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
                    if (Read(annotation, index) is { } box)
                    {
                        boxes.Add(box);
                    }
                }
                finally
                {
                    FPDFPage_CloseAnnot(annotation);
                }
            }

            return boxes;
        }
    }

    private PdfTextBox? Read(IntPtr annotation, int index)
    {
        if (FPDFAnnot_GetSubtype(annotation) != AnnotFreeText)
        {
            return null;
        }

        var flags = FPDFAnnot_GetFlags(annotation);
        if ((flags & (PdfTextBoxes.FlagHidden | PdfTextBoxes.FlagNoView)) != 0 || !FPDFAnnot_GetRect(annotation, out var rect))
        {
            return null;
        }

        if (!ToDevice(rect.Left, rect.Top, out var x1, out var y1) || !ToDevice(rect.Right, rect.Bottom, out var x2, out var y2))
        {
            return null;
        }

        var area = new PdfRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        var style = PdfTextStyle.Parse(StringValue(annotation, "DA"));
        var callout = FPDFAnnot_HasKey(annotation, "CL");
        var readOnly = (flags & PdfTextBoxes.FlagReadOnly) != 0;
        var locked = (flags & PdfTextBoxes.FlagLocked) != 0;
        var lockedContents = (flags & PdfTextBoxes.FlagLockedContents) != 0;
        return new PdfTextBox(
            Index,
            index,
            area,
            PdfTextBoxes.Normalize(StringValue(annotation, "Contents")),
            style.FontSize,
            style.Color,
            CanEditText: !callout && !readOnly && !lockedContents,
            CanMove: !callout && !readOnly && !locked,
            CanDelete: !readOnly && !locked);
    }

    private static string StringValue(IntPtr annotation, string key)
    {
        var size = (int)Math.Min(FPDFAnnot_GetStringValue(annotation, key, null, new CULong(0)).Value, 1024 * 1024);
        if (size <= 2)
        {
            return string.Empty;
        }

        var buffer = new byte[size];
        FPDFAnnot_GetStringValue(annotation, key, buffer, new CULong((uint)size));
        return Encoding.Unicode.GetString(buffer, 0, size - 2);
    }

    /// <summary>
    /// Page dessinée par PDFium, annotations comprises, en niveaux de gris (0 = noir), à la taille affichée en pixels
    /// à 100 % : sert aux tests à vérifier où une apparence est vraiment dessinée.
    /// </summary>
    internal (byte[] Pixels, int Width, int Height) RenderGray()
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_owner.IsDisposed || _page == IntPtr.Zero, this);
            var width = Math.Max(1, (int)Math.Round(Size.Width));
            var height = Math.Max(1, (int)Math.Round(Size.Height));
            var bitmap = FPDFBitmap_Create(width, height, 0);
            if (bitmap == IntPtr.Zero)
            {
                throw new InvalidOperationException("Image PDFium impossible à créer.");
            }

            try
            {
                FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bitmap, _page, 0, 0, width, height, 0, RenderAnnotations);
                var stride = FPDFBitmap_GetStride(bitmap);
                var buffer = FPDFBitmap_GetBuffer(bitmap);
                var row = new byte[stride];
                var pixels = new byte[width * height];
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(buffer + (y * stride), row, 0, stride);
                    for (var x = 0; x < width; x++)
                    {
                        // BGRx : moyenne des trois composantes.
                        pixels[(y * width) + x] = (byte)((row[x * 4] + row[(x * 4) + 1] + row[(x * 4) + 2]) / 3);
                    }
                }

                return (pixels, width, height);
            }
            finally
            {
                FPDFBitmap_Destroy(bitmap);
            }
        }
    }
}
