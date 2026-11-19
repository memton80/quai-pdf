using System.Runtime.InteropServices;
using static QuaiPdf.Core.Pdf.Text.PdfiumNative;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>
/// Texte d'un contenu PDF, lu par PDFium (le moteur PDF de Chrome) : position des caractères, rectangles à surligner
/// pour une sélection, texte d'une sélection avec ses fins de ligne. Rien n'est écrit : sert à sélectionner et copier.
/// </summary>
public sealed class PdfTextDocument : IDisposable
{
    private readonly Dictionary<int, PdfTextPage> _pages = [];
    private IntPtr _document;
    private unsafe void* _buffer;

    private unsafe PdfTextDocument(IntPtr document, void* buffer)
    {
        _document = document;
        _buffer = buffer;
        PageCount = FPDF_GetPageCount(document);
        CanCopy = (FPDF_GetDocPermissions(document) & PermissionExtract) != 0;
    }

    public int PageCount { get; }

    /// <summary>L'auteur du document autorise la copie du texte (droit « extraire le contenu »).</summary>
    public bool CanCopy { get; }

    internal bool IsDisposed => _document == IntPtr.Zero;

    /// <exception cref="PdfPasswordException">Mot de passe absent ou incorrect.</exception>
    /// <exception cref="PdfFileException">Contenu illisible.</exception>
    public static unsafe PdfTextDocument Open(byte[] content, string? password)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (Gate)
        {
            EnsureInitialized();

            // PDFium lit le contenu au fur et à mesure : il reste en mémoire native jusqu'à la fermeture.
            var buffer = NativeMemory.Alloc((nuint)Math.Max(1, content.Length));
            content.AsSpan().CopyTo(new Span<byte>(buffer, content.Length));
            var document = FPDF_LoadMemDocument64((IntPtr)buffer, (nuint)content.Length, password);
            if (document == IntPtr.Zero)
            {
                var error = FPDF_GetLastError();
                NativeMemory.Free(buffer);
                throw error == ErrorPassword
                    ? new PdfPasswordException("Mot de passe absent ou incorrect.")
                    : new PdfFileException(PdfProblem.Unreadable, $"PDFium ne peut pas lire ce contenu (erreur {error}).");
            }

            return new PdfTextDocument(document, buffer);
        }
    }

    /// <summary>Texte d'une page, chargé à la première demande puis gardé.</summary>
    public PdfTextPage GetPage(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PageCount);
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (!_pages.TryGetValue(index, out var page))
            {
                var handle = FPDF_LoadPage(_document, index);
                var text = handle == IntPtr.Zero ? IntPtr.Zero : FPDFText_LoadPage(handle);
                page = new PdfTextPage(this, handle, text);
                _pages[index] = page;
            }

            return page;
        }
    }

    public unsafe void Dispose()
    {
        lock (Gate)
        {
            if (IsDisposed)
            {
                return;
            }

            foreach (var page in _pages.Values)
            {
                page.Close();
            }

            _pages.Clear();
            FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
    }
}
