using System.Runtime.InteropServices;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>
/// Fonctions de PDFium utilisées par Quai PDF (API C publique, <c>fpdfview.h</c>, <c>fpdf_text.h</c> et
/// <c>fpdf_doc.h</c>). La bibliothèque n'est pas prévue pour plusieurs fils : tout appel passe par <see cref="Gate"/>.
/// Le type C <c>unsigned long</c> fait 32 bits sous Windows et 64 bits sous Linux : <see cref="CULong"/>.
/// </summary>
internal static class PdfiumNative
{
    private const string Library = "pdfium";

    /// <summary>Erreur de <c>FPDF_GetLastError</c> : mot de passe absent ou incorrect.</summary>
    public const uint ErrorPassword = 4;

    /// <summary>Action « aller à une destination du document » (<c>PDFACTION_GOTO</c>).</summary>
    public const uint ActionGoTo = 1;

    /// <summary>Droit « copier ou extraire le texte » (bit 5 des permissions, ISO 32000-1, tableau 22).</summary>
    public const uint PermissionExtract = 1 << 4;

    private static readonly Lazy<bool> Initialized = new(() =>
    {
        FPDF_InitLibrary();
        return true;
    });

    public static Lock Gate { get; } = new();

    /// <summary>Initialise PDFium une fois pour toutes (à appeler sous <see cref="Gate"/>).</summary>
    public static void EnsureInitialized() => _ = Initialized.Value;

    [DllImport(Library)]
    private static extern void FPDF_InitLibrary();

    [DllImport(Library)]
    public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    [DllImport(Library)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(Library)]
    public static extern uint FPDF_GetLastError();

    [DllImport(Library)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(Library)]
    public static extern uint FPDF_GetDocPermissions(IntPtr document);

    [DllImport(Library)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);

    [DllImport(Library)]
    public static extern void FPDF_ClosePage(IntPtr page);

    [DllImport(Library)]
    public static extern float FPDF_GetPageWidthF(IntPtr page);

    [DllImport(Library)]
    public static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDF_PageToDevice(
        IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, double pageX, double pageY, out int deviceX, out int deviceY);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDF_DeviceToPage(
        IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int deviceX, int deviceY, out double pageX, out double pageY);

    [DllImport(Library)]
    public static extern IntPtr FPDFText_LoadPage(IntPtr page);

    [DllImport(Library)]
    public static extern void FPDFText_ClosePage(IntPtr textPage);

    [DllImport(Library)]
    public static extern int FPDFText_CountChars(IntPtr textPage);

    [DllImport(Library)]
    public static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDFText_GetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);

    [DllImport(Library)]
    public static extern int FPDFText_GetCharIndexAtPos(IntPtr textPage, double x, double y, double xTolerance, double yTolerance);

    [DllImport(Library)]
    public static extern int FPDFText_CountRects(IntPtr textPage, int start, int count);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDFText_GetRect(IntPtr textPage, int index, out double left, out double top, out double right, out double bottom);

    [DllImport(Library)]
    public static extern int FPDFText_GetText(IntPtr textPage, int start, int count, [Out] ushort[] buffer);

    // ----- Plan du document (signets), fpdf_doc.h -----

    [DllImport(Library)]
    public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);

    [DllImport(Library)]
    public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);

    /// <summary>Titre en UTF-16LE, zéro final compris ; retourne la taille nécessaire en octets.</summary>
    [DllImport(Library)]
    public static extern CULong FPDFBookmark_GetTitle(IntPtr bookmark, [Out] byte[]? buffer, CULong length);

    [DllImport(Library)]
    public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);

    [DllImport(Library)]
    public static extern IntPtr FPDFBookmark_GetAction(IntPtr bookmark);

    [DllImport(Library)]
    public static extern CULong FPDFAction_GetType(IntPtr action);

    [DllImport(Library)]
    public static extern IntPtr FPDFAction_GetDest(IntPtr document, IntPtr action);

    [DllImport(Library)]
    public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);

    // ----- Annotations, fpdf_annot.h -----

    /// <summary>Sous-type <c>/FreeText</c> (<c>FPDF_ANNOT_FREETEXT</c>).</summary>
    public const int AnnotFreeText = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct FsRectF
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
    }

    [DllImport(Library)]
    public static extern int FPDFPage_GetAnnotCount(IntPtr page);

    [DllImport(Library)]
    public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);

    [DllImport(Library)]
    public static extern void FPDFPage_CloseAnnot(IntPtr annotation);

    [DllImport(Library)]
    public static extern int FPDFAnnot_GetSubtype(IntPtr annotation);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDFAnnot_GetRect(IntPtr annotation, out FsRectF rect);

    [DllImport(Library)]
    public static extern int FPDFAnnot_GetFlags(IntPtr annotation);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDFAnnot_HasKey(IntPtr annotation, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    /// <summary>Sous-type <c>/Highlight</c> (<c>FPDF_ANNOT_HIGHLIGHT</c>).</summary>
    public const int AnnotHighlight = 9;

    /// <summary>Quadrilatère d'un surlignage (<c>FS_QUADPOINTSF</c>), dans le repère du PDF.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FsQuadPointsF
    {
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;
        public float X3;
        public float Y3;
        public float X4;
        public float Y4;
    }

    [DllImport(Library)]
    public static extern nuint FPDFAnnot_CountAttachmentPoints(IntPtr annotation);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDFAnnot_GetAttachmentPoints(IntPtr annotation, nuint index, out FsQuadPointsF quad);

    /// <summary>Valeur texte en UTF-16LE, zéro final compris ; retourne la taille nécessaire en octets.</summary>
    [DllImport(Library)]
    public static extern CULong FPDFAnnot_GetStringValue(IntPtr annotation, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [Out] byte[]? buffer, CULong length);

    // ----- Rendu (tests : vérifier qu'une apparence est dessinée au bon endroit), fpdfview.h -----

    /// <summary>Dessiner aussi les annotations (<c>FPDF_ANNOT</c>).</summary>
    public const int RenderAnnotations = 0x01;

    [DllImport(Library)]
    public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

    [DllImport(Library)]
    public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [DllImport(Library)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [DllImport(Library)]
    public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [DllImport(Library)]
    public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

    [DllImport(Library)]
    public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
}
