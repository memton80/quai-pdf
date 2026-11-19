using System.Runtime.InteropServices;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>
/// Fonctions de PDFium utilisées par Quai PDF (API C publique, <c>fpdfview.h</c> et <c>fpdf_text.h</c>).
/// La bibliothèque n'est pas prévue pour plusieurs fils : tout appel passe par <see cref="Gate"/>.
/// </summary>
internal static class PdfiumNative
{
    private const string Library = "pdfium";

    /// <summary>Erreur de <c>FPDF_GetLastError</c> : mot de passe absent ou incorrect.</summary>
    public const uint ErrorPassword = 4;

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
}
