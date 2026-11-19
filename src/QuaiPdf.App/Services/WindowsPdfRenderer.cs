using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using QuaiPdf.Core.Pdf;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace QuaiPdf.App.Services;

/// <summary>
/// Affichage des pages par le moteur PDF intégré à Windows (<c>Windows.Data.Pdf</c>) : rendu natif, sans navigateur
/// ni application web cachée, sans bibliothèque à embarquer.
/// </summary>
internal sealed class WindowsPdfRenderer : IPdfRenderer
{
    // HRESULT_FROM_WIN32(ERROR_WRONG_PASSWORD) : mot de passe absent ou incorrect.
    private const int WrongPassword = unchecked((int)0x8007052B);

    public async Task<IRenderedPdf> LoadAsync(byte[] content, string? password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stream = new InMemoryRandomAccessStream();
        try
        {
            await stream.WriteAsync(content.AsBuffer());
            stream.Seek(0);
            cancellationToken.ThrowIfCancellationRequested();

            var document = password is null
                ? await WinPdfDocument.LoadFromStreamAsync(stream)
                : await WinPdfDocument.LoadFromStreamAsync(stream, password);

            var sizes = new List<PdfPageSize>((int)document.PageCount);
            for (uint i = 0; i < document.PageCount; i++)
            {
                using var page = document.GetPage(i);
                sizes.Add(new PdfPageSize(page.Size.Width, page.Size.Height));
            }

            if (sizes.Count == 0)
            {
                throw new PdfFileException(PdfProblem.Unreadable, "Document sans page.");
            }

            return new WindowsRenderedPdf(document, stream, sizes);
        }
        catch (Exception ex) when (ex.HResult == WrongPassword)
        {
            stream.Dispose();
            throw new PdfPasswordException("Mot de passe absent ou incorrect.", ex);
        }
        catch (Exception ex) when (ex is not (PdfFileException or OperationCanceledException))
        {
            stream.Dispose();
            throw new PdfFileException(PdfProblem.Unreadable, "Le moteur PDF de Windows ne peut pas lire ce contenu.", ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}

/// <summary>Document chargé dans le moteur de Windows ; dessine les pages à la demande.</summary>
internal sealed class WindowsRenderedPdf : IRenderedPdf
{
    // Plafond d'une image de page (pixels) : au-delà, l'image est agrandie à l'écran plutôt que dessinée plus finement.
    private const double MaxPixels = 16_000_000;

    // Deux pages dessinées à la fois au plus : la mémoire reste bornée pendant un défilement rapide.
    private const int Parallelism = 2;

    private readonly WinPdfDocument _document;
    private readonly InMemoryRandomAccessStream _stream;
    private readonly SemaphoreSlim _gate = new(Parallelism, Parallelism);
    private bool _disposed;

    public WindowsRenderedPdf(WinPdfDocument document, InMemoryRandomAccessStream stream, IReadOnlyList<PdfPageSize> pages)
    {
        _document = document;
        _stream = stream;
        Pages = pages;
    }

    public IReadOnlyList<PdfPageSize> Pages { get; }

    /// <summary>
    /// Image d'une page à la largeur voulue (pixels physiques) ; <c>null</c> si ce contenu a été remplacé entre-temps.
    /// À appeler depuis le fil d'interface (création de l'image).
    /// </summary>
    public async Task<BitmapImage?> RenderAsync(int index, double pixelWidth)
    {
        if (_disposed || index < 0 || index >= Pages.Count)
        {
            return null;
        }

        var size = Pages[index];
        var maxWidth = size.Height > 0 ? Math.Sqrt(MaxPixels * size.Width / size.Height) : pixelWidth;
        var width = (uint)Math.Clamp(Math.Round(pixelWidth), 1, Math.Max(1, maxWidth));

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return null;
            }

            using var page = _document.GetPage((uint)index);
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions
            {
                DestinationWidth = width,
                BackgroundColor = Microsoft.UI.Colors.White,
            });
            output.Seek(0);

            var image = new BitmapImage();
            await image.SetSourceAsync(output);
            return _disposed ? null : image;
        }
        finally
        {
            _gate.Release();
            if (_disposed && _gate.CurrentCount == Parallelism)
            {
                _stream.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Une page en cours de dessin libère le contenu à la fin de son rendu (voir RenderAsync).
        if (_gate.CurrentCount == Parallelism)
        {
            _stream.Dispose();
        }
    }
}
