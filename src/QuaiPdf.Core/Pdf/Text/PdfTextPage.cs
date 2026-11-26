using static QuaiPdf.Core.Pdf.Text.PdfiumNative;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>
/// Texte d'une page. Les positions sont celles de la page affichée (<see cref="PdfPoint"/> : pixels indépendants à
/// 100 %, origine en haut à gauche, rotation comprise) ; PDFium fait la conversion depuis le repère du PDF.
/// </summary>
public sealed partial class PdfTextPage
{
    // Unités d'« appareil » par pixel indépendant : PDFium convertit en coordonnées entières, d'où cette finesse.
    private const int Precision = 100;

    // Recherche du caractère le plus proche (distances horizontale et verticale, en points PDF) : d'abord sur la
    // même ligne, aussi loin qu'il le faut (à droite d'une fin de ligne : son dernier caractère), puis de plus en
    // plus loin dans toutes les directions.
    private static readonly (double X, double Y)[] NearestTolerances =
        [(2, 2), (100_000, 2), (8, 8), (24, 24), (72, 72), (216, 216), (100_000, 100_000)];

    private readonly PdfTextDocument _owner;
    private IntPtr _page;
    private IntPtr _text;

    internal PdfTextPage(PdfTextDocument owner, int index, IntPtr page, IntPtr text)
    {
        _owner = owner;
        Index = index;
        _page = page;
        _text = text;
        if (page != IntPtr.Zero)
        {
            Size = PdfPageSize.FromPoints(FPDF_GetPageWidthF(page), FPDF_GetPageHeightF(page));
        }

        CharCount = text == IntPtr.Zero ? 0 : Math.Max(0, FPDFText_CountChars(text));
    }

    /// <summary>Place de la page dans le document (à partir de 0).</summary>
    public int Index { get; }

    /// <summary>Taille de la page affichée (rotation comprise), en pixels indépendants à 100 %.</summary>
    public PdfPageSize Size { get; }

    /// <summary>Nombre de caractères du texte de la page (0 pour une page scannée sans texte).</summary>
    public int CharCount { get; }

    /// <summary>Caractère sous ce point (à <paramref name="tolerance"/> pixels près), ou -1.</summary>
    public int HitTest(PdfPoint point, double tolerance)
    {
        lock (Gate)
        {
            if (!IsUsable || !ToPage(point, out var x, out var y))
            {
                return -1;
            }

            var points = tolerance / PdfPageSize.PixelsPerPoint;
            var index = FPDFText_GetCharIndexAtPos(_text, x, y, points, points);
            return index >= 0 && index < CharCount ? index : -1;
        }
    }

    /// <summary>Caractère le plus proche de ce point (pour étendre une sélection), ou -1 si la page n'a pas de texte.</summary>
    public int NearestChar(PdfPoint point)
    {
        if (CharCount == 0)
        {
            return -1;
        }

        lock (Gate)
        {
            if (!IsUsable || !ToPage(point, out var x, out var y))
            {
                return -1;
            }

            var index = Nearest(x, y);
            if (index >= 0)
            {
                return index;
            }
        }

        return point.Y < Size.Height / 2 ? 0 : CharCount - 1;
    }

    /// <summary>
    /// Place du curseur la plus proche de ce point, de 0 à <see cref="CharCount"/> : devant le caractère le plus
    /// proche, ou derrière si le point dépasse son milieu. -1 si la page n'a pas de texte.
    /// </summary>
    public int CaretAt(PdfPoint point)
    {
        if (CharCount == 0)
        {
            return -1;
        }

        lock (Gate)
        {
            if (IsUsable && ToPage(point, out var x, out var y))
            {
                var index = Nearest(x, y);
                if (index >= 0)
                {
                    // Milieu du caractère dans le repère du PDF : le sens d'écriture ne dépend pas de la rotation de la page.
                    return FPDFText_GetCharBox(_text, index, out var left, out var right, out _, out _) && right > left
                        && x > (left + right) / 2
                        ? index + 1
                        : index;
                }
            }
        }

        return point.Y < Size.Height / 2 ? 0 : CharCount;
    }

    /// <summary>Mot autour d'un caractère (lettres et chiffres) ; le caractère seul s'il n'en fait pas partie.</summary>
    public (int Start, int End) WordAt(int index)
    {
        if (index < 0 || index >= CharCount)
        {
            return (index, index);
        }

        lock (Gate)
        {
            if (!IsUsable || !IsWordChar(index))
            {
                return (index, index);
            }

            var start = index;
            while (start > 0 && IsWordChar(start - 1))
            {
                start--;
            }

            var end = index;
            while (end < CharCount - 1 && IsWordChar(end + 1))
            {
                end++;
            }

            return (start, end);
        }
    }

    /// <summary>Rectangles à surligner pour les caractères <paramref name="start"/> à <paramref name="end"/> (compris).</summary>
    public IReadOnlyList<PdfRect> Rects(int start, int end)
    {
        if (!TryRange(ref start, ref end))
        {
            return [];
        }

        lock (Gate)
        {
            if (!IsUsable)
            {
                return [];
            }

            var count = FPDFText_CountRects(_text, start, end - start + 1);
            var rects = new List<PdfRect>(Math.Max(0, count));
            for (var i = 0; i < count; i++)
            {
                if (FPDFText_GetRect(_text, i, out var left, out var top, out var right, out var bottom)
                    && ToDevice(left, top, out var x1, out var y1)
                    && ToDevice(right, bottom, out var x2, out var y2))
                {
                    rects.Add(new PdfRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1)));
                }
            }

            return rects;
        }
    }

    /// <summary>Texte des caractères <paramref name="start"/> à <paramref name="end"/> (compris), fins de ligne comprises.</summary>
    public string Text(int start, int end)
    {
        if (!TryRange(ref start, ref end))
        {
            return string.Empty;
        }

        lock (Gate)
        {
            if (!IsUsable)
            {
                return string.Empty;
            }

            var count = end - start + 1;
            var buffer = new ushort[count + 1];
            var written = FPDFText_GetText(_text, start, count, buffer);
            var length = Math.Clamp(written - 1, 0, count);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)buffer[i];
            }

            return new string(chars);
        }
    }

    public string AllText() => CharCount == 0 ? string.Empty : Text(0, CharCount - 1);

    internal void Close()
    {
        if (_text != IntPtr.Zero)
        {
            FPDFText_ClosePage(_text);
            _text = IntPtr.Zero;
        }

        if (_page != IntPtr.Zero)
        {
            FPDF_ClosePage(_page);
            _page = IntPtr.Zero;
        }
    }

    private bool IsUsable => !_owner.IsDisposed && _text != IntPtr.Zero && _page != IntPtr.Zero;

    private int Nearest(double x, double y)
    {
        foreach (var (toleranceX, toleranceY) in NearestTolerances)
        {
            var index = FPDFText_GetCharIndexAtPos(_text, x, y, toleranceX, toleranceY);
            if (index >= 0 && index < CharCount)
            {
                return index;
            }
        }

        return -1;
    }

    private bool TryRange(ref int start, ref int end)
    {
        if (CharCount == 0)
        {
            return false;
        }

        (start, end) = (Math.Clamp(Math.Min(start, end), 0, CharCount - 1), Math.Clamp(Math.Max(start, end), 0, CharCount - 1));
        return true;
    }

    private bool IsWordChar(int index)
    {
        var code = FPDFText_GetUnicode(_text, index);
        if (code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
        {
            return false;
        }

        var text = char.ConvertFromUtf32((int)code);
        return char.IsLetterOrDigit(text, 0) || text is "'" or "’" or "-" && index > 0;
    }

    private (int Width, int Height) DeviceSize =>
        ((int)Math.Round(Size.Width * Precision), (int)Math.Round(Size.Height * Precision));

    private bool ToPage(PdfPoint point, out double x, out double y)
    {
        var (width, height) = DeviceSize;
        return FPDF_DeviceToPage(
            _page, 0, 0, width, height, 0, (int)Math.Round(point.X * Precision), (int)Math.Round(point.Y * Precision), out x, out y);
    }

    private bool ToDevice(double pageX, double pageY, out double x, out double y)
    {
        var (width, height) = DeviceSize;
        var ok = FPDF_PageToDevice(_page, 0, 0, width, height, 0, pageX, pageY, out var deviceX, out var deviceY);
        x = deviceX / (double)Precision;
        y = deviceY / (double)Precision;
        return ok;
    }
}
