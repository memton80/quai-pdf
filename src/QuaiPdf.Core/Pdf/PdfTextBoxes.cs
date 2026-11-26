using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Zones de texte posées sur les pages (annotations <c>/FreeText</c>, ISO 32000-1, 12.5.6.6) : ajout, modification
/// (texte, taille, couleur, position) et suppression, avec PDFsharp, comme les autres opérations : un contenu en entrée,
/// un nouveau contenu en sortie.
/// <para>
/// Pour être relues partout à l'identique, les zones ont toujours une apparence (<c>/AP</c>) : c'est elle que dessinent
/// Chrome, Edge, Firefox, Acrobat et le moteur de Windows. Elle est écrite en Helvetica (police standard, rien à
/// intégrer), comme le fait Firefox, et tournée pour rester droite à l'écran sur une page tournée. Le texte exact (tout
/// Unicode) est aussi gardé dans <c>/Contents</c>, et la taille et la couleur dans <c>/DA</c> et <c>/DS</c>. Le texte
/// enrichi d'Acrobat (<c>/RC</c>), qui ne correspondrait plus, est retiré à la modification.
/// </para>
/// <para>
/// Une zone n'est jamais coupée automatiquement : chaque ligne du texte est une ligne de la zone, qui prend la taille de
/// son contenu (comme dans Firefox). Les positions sont données comme à l'écran : pixels à 100 %, origine en haut à
/// gauche de la page affichée, quelle que soit la rotation de la page.
/// </para>
/// </summary>
public static class PdfTextBoxes
{
    /// <summary>Marge intérieure, en points.</summary>
    public const double Padding = 2;

    /// <summary>Hauteur d'une ligne, en multiple de la taille du texte.</summary>
    public const double LineHeight = 1.25;

    public const int MaxTextLength = 10_000;

    // Drapeaux d'annotation (ISO 32000-1, tableau 165).
    internal const int FlagHidden = 1 << 1;
    internal const int FlagPrint = 1 << 2;
    internal const int FlagNoView = 1 << 5;
    internal const int FlagReadOnly = 1 << 6;
    internal const int FlagLocked = 1 << 7;
    internal const int FlagLockedContents = 1 << 9;

    private const string FontResource = "/Helv";

    /// <summary>Taille affichée d'une zone pour ce texte (pixels à 100 %).</summary>
    public static PdfPageSize Measure(string text, double fontSize)
    {
        var (width, height) = MeasurePoints(Lines(Normalize(text)), fontSize);
        return PdfPageSize.FromPoints(width, height);
    }

    /// <summary>Le texte peut-il être écrit tel quel dans l'apparence (sinon des caractères y deviennent des « ? ») ?</summary>
    public static bool IsFullySupported(string text) => Normalize(text).All(c => c == '\n' || Helvetica.CanEncode(c));

    /// <summary>Ajoute une zone de texte dont le coin haut gauche est <paramref name="topLeft"/> (pixels à 100 %).</summary>
    public static byte[] Add(byte[] content, int page, PdfPoint topLeft, string text, PdfTextStyle style)
    {
        var normalized = Validate(text, style);
        return PdfOperations.Transform(content, document =>
        {
            var pdfPage = PageAt(document, page);
            var annotation = new PdfDictionary(document);
            annotation.Elements["/Type"] = new PdfName("/Annot");
            annotation.Elements["/Subtype"] = new PdfName("/FreeText");
            annotation.Elements["/NM"] = new PdfString($"quaipdf-{Guid.NewGuid():N}");
            annotation.Elements["/CreationDate"] = new PdfString(DateString());
            annotation.Elements["/F"] = new PdfInteger(FlagPrint);
            annotation.Elements["/Border"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(0));
            if (pdfPage.Reference is { } pageReference)
            {
                annotation.Elements["/P"] = pageReference;
            }

            Fill(document, annotation, PageGeometry.Of(pdfPage), topLeft, normalized, style);
            document.Internals.AddObject(annotation);
            Annotations(document, pdfPage, create: true)!.Elements.Add(annotation.Reference!);
        });
    }

    /// <summary>
    /// Modifie une zone de texte (texte, taille, couleur, coin haut gauche) : son apparence est refaite, le reste de ses
    /// propriétés (nom, auteur, dates, bulle associée…) est gardé.
    /// </summary>
    /// <exception cref="InvalidOperationException">Ce n'est pas une zone de texte, ou son auteur l'a verrouillée.</exception>
    public static byte[] Update(byte[] content, int page, int index, PdfPoint topLeft, string text, PdfTextStyle style)
    {
        var normalized = Validate(text, style);
        return PdfOperations.Transform(content, document =>
        {
            var pdfPage = PageAt(document, page);
            var annotation = FreeTextAt(document, pdfPage, index);
            var flags = annotation.Elements.GetInteger("/F");
            if (annotation.Elements.ContainsKey("/CL"))
            {
                throw new InvalidOperationException("Zone de texte avec une ligne de rappel : elle ne se modifie pas ici.");
            }

            var oldText = Normalize(ReadText(annotation));
            var oldStyle = PdfTextStyle.Parse(annotation.Elements.GetString("/DA"));
            if ((flags & (FlagReadOnly | FlagLockedContents)) != 0 && (oldText != normalized || oldStyle != style))
            {
                throw new InvalidOperationException("L'auteur de cette zone de texte en a verrouillé le contenu.");
            }

            var geometry = PageGeometry.Of(pdfPage);
            var area = AreaOf(annotation, geometry);
            var moved = area is not { } current
                || Math.Abs(current.X - topLeft.X) > 0.5 || Math.Abs(current.Y - topLeft.Y) > 0.5;
            if ((flags & (FlagReadOnly | FlagLocked)) != 0 && moved)
            {
                throw new InvalidOperationException("L'auteur de cette zone de texte l'a verrouillée à sa place.");
            }

            Fill(document, annotation, geometry, topLeft, normalized, style);
        });
    }

    /// <summary>Supprime une zone de texte, avec sa bulle (<c>/Popup</c>) et les réponses qui s'y rattachent (<c>/IRT</c>).</summary>
    /// <exception cref="InvalidOperationException">Ce n'est pas une zone de texte, ou son auteur l'a verrouillée.</exception>
    public static byte[] Delete(byte[] content, int page, int index)
    {
        return PdfOperations.Transform(content, document =>
        {
            var pdfPage = PageAt(document, page);
            var annotation = FreeTextAt(document, pdfPage, index);
            if ((annotation.Elements.GetInteger("/F") & (FlagReadOnly | FlagLocked)) != 0)
            {
                throw new InvalidOperationException("L'auteur de cette zone de texte l'a verrouillée.");
            }

            var annotations = Annotations(document, pdfPage, create: false)!;
            var removed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { annotation };
            if (Resolve(annotation.Elements["/Popup"]) is PdfDictionary popup)
            {
                removed.Add(popup);
            }

            foreach (var item in annotations.Elements.ToList())
            {
                if (Resolve(item) is PdfDictionary other && Resolve(other.Elements["/IRT"]) is PdfDictionary parent
                    && ReferenceEquals(parent, annotation))
                {
                    removed.Add(other);
                }
            }

            for (var i = annotations.Elements.Count - 1; i >= 0; i--)
            {
                if (Resolve(annotations.Elements[i]) is PdfDictionary item && removed.Contains(item))
                {
                    annotations.Elements.RemoveAt(i);
                }
            }
        });
    }

    // ----- Contenu et apparence -----

    /// <summary>Remplit (ou remplace) tout ce qui dépend du texte, de son style et de sa place.</summary>
    private static void Fill(PdfDocument document, PdfDictionary annotation, PageGeometry geometry, PdfPoint topLeft, string text, PdfTextStyle style)
    {
        var lines = Lines(text);
        var (width, height) = MeasurePoints(lines, style.FontSize);
        var x = topLeft.X / PdfPageSize.PixelsPerPoint;
        var y = topLeft.Y / PdfPageSize.PixelsPerPoint;
        var (left, bottom, right, top) = geometry.ToUser(x, y, width, height);

        annotation.Elements["/Rect"] = Numbers(document, left, bottom, right, top);
        annotation.Elements["/Contents"] = new PdfString(text, PdfStringEncoding.Unicode);
        annotation.Elements["/DA"] = new PdfString(DefaultAppearance(style));
        annotation.Elements["/DS"] = new PdfString(DefaultStyle(style));
        annotation.Elements["/Rotate"] = new PdfInteger(geometry.Rotation);
        annotation.Elements["/M"] = new PdfString(DateString());
        annotation.Elements.Remove("/RC");

        var appearance = new PdfDictionary(document);
        appearance.Elements["/N"] = AppearanceStream(document, lines, width, height, geometry.Rotation, style).Reference!;
        annotation.Elements["/AP"] = appearance;
        annotation.Elements.Remove("/AS");
    }

    /// <summary>
    /// Apparence : une forme de la taille de la zone (dans le sens de lecture), tournée par sa matrice pour rester
    /// droite à l'écran ; le lecteur la place dans <c>/Rect</c> (ISO 32000-1, 12.5.5).
    /// </summary>
    private static PdfDictionary AppearanceStream(PdfDocument document, IReadOnlyList<string> lines, double width, double height, int rotation, PdfTextStyle style)
    {
        var font = new PdfDictionary(document);
        font.Elements["/Type"] = new PdfName("/Font");
        font.Elements["/Subtype"] = new PdfName("/Type1");
        font.Elements["/BaseFont"] = new PdfName("/Helvetica");
        font.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        var fonts = new PdfDictionary(document);
        fonts.Elements[FontResource] = font;
        var resources = new PdfDictionary(document);
        resources.Elements["/Font"] = fonts;

        var form = new PdfDictionary(document);
        form.Elements["/Type"] = new PdfName("/XObject");
        form.Elements["/Subtype"] = new PdfName("/Form");
        form.Elements["/FormType"] = new PdfInteger(1);
        form.Elements["/BBox"] = Numbers(document, 0, 0, width, height);
        form.Elements["/Matrix"] = rotation switch
        {
            90 => Numbers(document, 0, 1, -1, 0, 0, 0),
            180 => Numbers(document, -1, 0, 0, -1, 0, 0),
            270 => Numbers(document, 0, -1, 1, 0, 0, 0),
            _ => Numbers(document, 1, 0, 0, 1, 0, 0),
        };
        form.Elements["/Resources"] = resources;
        form.CreateStream(ContentStream(lines, height, style));
        document.Internals.AddObject(form);
        return form;
    }

    private static byte[] ContentStream(IReadOnlyList<string> lines, double height, PdfTextStyle style)
    {
        var size = style.FontSize;
        var lineHeight = size * LineHeight;
        var baseline = ((lineHeight - ((Helvetica.Ascent + Helvetica.Descent) * size / 1000)) / 2) + (Helvetica.Ascent * size / 1000);
        var stream = new MemoryStream();
        Write(stream, "q\nBT\n");
        Write(stream, Invariant($"{FontResource} {size:0.###} Tf\n{style.Color.R:0.###} {style.Color.G:0.###} {style.Color.B:0.###} rg\n"));
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            Write(stream, Invariant($"1 0 0 1 {Padding:0.###} {height - Padding - (i * lineHeight) - baseline:0.###} Tm\n("));
            foreach (var code in Helvetica.EncodeText(lines[i]))
            {
                switch (code)
                {
                    case (byte)'(' or (byte)')' or (byte)'\\':
                        stream.WriteByte((byte)'\\');
                        stream.WriteByte(code);
                        break;
                    case < 0x20 or >= 0x7F:
                        Write(stream, "\\" + Convert.ToString(code, 8).PadLeft(3, '0'));
                        break;
                    default:
                        stream.WriteByte(code);
                        break;
                }
            }

            Write(stream, ") Tj\n");
        }

        Write(stream, "ET\nQ\n");
        return stream.ToArray();

        static void Write(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
    }

    private static (double Width, double Height) MeasurePoints(IReadOnlyList<string> lines, double fontSize)
    {
        var width = lines.Max(line => Helvetica.Width(line, fontSize));
        var height = lines.Count * fontSize * LineHeight;
        return (Math.Max(width, fontSize / 2) + (2 * Padding), height + (2 * Padding));
    }

    /// <summary>Apparence par défaut (<c>/DA</c>) : police, taille, couleur, dans la syntaxe des flux PDF.</summary>
    internal static string DefaultAppearance(PdfTextStyle style) =>
        Invariant($"{FontResource} {style.FontSize:0.##} Tf {style.Color.R:0.###} {style.Color.G:0.###} {style.Color.B:0.###} rg");

    /// <summary>Style par défaut (<c>/DS</c>), comme l'écrit Acrobat.</summary>
    private static string DefaultStyle(PdfTextStyle style) =>
        Invariant($"font: Helvetica,sans-serif {style.FontSize:0.0}pt; text-align:left; color:{style.Color.ToHex()}");

    // ----- Lecture et validation -----

    /// <summary>Fins de ligne de Windows, du Mac ou d'Unix : toujours « \n ».</summary>
    internal static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static List<string> Lines(string text) => [.. text.Split('\n')];

    private static string Validate(string text, PdfTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Une zone de texte ne peut pas être vide.", nameof(text));
        }

        if (normalized.Length > MaxTextLength)
        {
            throw new ArgumentException($"Texte trop long ({normalized.Length} caractères, {MaxTextLength} au plus).", nameof(text));
        }

        if (style.FontSize is < PdfTextStyle.MinFontSize or > PdfTextStyle.MaxFontSize || !double.IsFinite(style.FontSize))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style.FontSize, "Taille de texte hors limites.");
        }

        return normalized;
    }

    private static string ReadText(PdfDictionary annotation) =>
        annotation.Elements["/Contents"] is PdfString contents ? contents.Value : string.Empty;

    private static PdfPage PageAt(PdfDocument document, int page)
    {
        if (page < 0 || page >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "Numéro de page hors du document.");
        }

        return document.Pages[page];
    }

    /// <summary>Liste <c>/Annots</c> de la page (directe ou référencée), créée au besoin.</summary>
    private static PdfArray? Annotations(PdfDocument document, PdfPage page, bool create)
    {
        if (Resolve(page.Elements["/Annots"]) is PdfArray annotations)
        {
            return annotations;
        }

        if (!create)
        {
            return null;
        }

        var created = new PdfArray(document);
        page.Elements["/Annots"] = created;
        return created;
    }

    private static PdfDictionary FreeTextAt(PdfDocument document, PdfPage page, int index)
    {
        var annotations = Annotations(document, page, create: false);
        if (annotations is null || index < 0 || index >= annotations.Elements.Count
            || Resolve(annotations.Elements[index]) is not PdfDictionary annotation
            || annotation.Elements.GetName("/Subtype") != "/FreeText")
        {
            throw new InvalidOperationException("Cette zone de texte n'existe plus dans le document.");
        }

        return annotation;
    }

    /// <summary>Rectangle affiché actuel d'une zone (pixels à 100 %), ou <c>null</c> s'il est illisible.</summary>
    private static PdfRect? AreaOf(PdfDictionary annotation, PageGeometry geometry)
    {
        if (Resolve(annotation.Elements["/Rect"]) is not PdfArray { Elements.Count: 4 } rect)
        {
            return null;
        }

        var values = Enumerable.Range(0, 4).Select(i => rect.Elements.GetReal(i)).ToArray();
        var (x, y, width, height) = geometry.ToDisplay(values[0], values[1], values[2], values[3]);
        return new PdfRect(x * PdfPageSize.PixelsPerPoint, y * PdfPageSize.PixelsPerPoint, width * PdfPageSize.PixelsPerPoint, height * PdfPageSize.PixelsPerPoint);
    }

    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

    private static PdfArray Numbers(PdfDocument document, params double[] values) =>
        new(document, [.. values.Select(v => (PdfItem)new PdfReal(Math.Round(v, 4)))]);

    private static string DateString() => "D:" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Repère d'une page : zone visible (<c>/CropBox</c>, sinon <c>/MediaBox</c>) et rotation. Passe des points affichés
/// (origine en haut à gauche de la page telle qu'on la voit) aux coordonnées du PDF, et inversement.
/// </summary>
internal readonly record struct PageGeometry(double Left, double Bottom, double Right, double Top, int Rotation)
{
    public static PageGeometry Of(PdfPage page)
    {
        var box = page.Elements.GetRectangle("/CropBox");
        if (box.IsZero)
        {
            box = page.MediaBox;
        }

        return new PageGeometry(
            Math.Min(box.X1, box.X2), Math.Min(box.Y1, box.Y2), Math.Max(box.X1, box.X2), Math.Max(box.Y1, box.Y2),
            PdfOperations.NormalizeRotation(page.Rotate));
    }

    /// <summary>Un point affiché (en points) vers le repère du PDF.</summary>
    public (double X, double Y) ToUser(double x, double y) => Rotation switch
    {
        90 => (Left + y, Bottom + x),
        180 => (Right - x, Bottom + y),
        270 => (Right - y, Top - x),
        _ => (Left + x, Top - y),
    };

    /// <summary>Un rectangle affiché (en points) vers le repère du PDF : gauche, bas, droite, haut.</summary>
    public (double Left, double Bottom, double Right, double Top) ToUser(double x, double y, double width, double height)
    {
        var (x1, y1) = ToUser(x, y);
        var (x2, y2) = ToUser(x + width, y + height);
        return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>Un rectangle du PDF (deux coins quelconques) vers l'affichage (en points) : x, y, largeur, hauteur.</summary>
    public (double X, double Y, double Width, double Height) ToDisplay(double x1, double y1, double x2, double y2)
    {
        var (ax, ay) = FromUser(x1, y1);
        var (bx, by) = FromUser(x2, y2);
        return (Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax), Math.Abs(by - ay));
    }

    private (double X, double Y) FromUser(double x, double y) => Rotation switch
    {
        90 => (y - Bottom, x - Left),
        180 => (Right - x, y - Bottom),
        270 => (Top - y, Right - x),
        _ => (x - Left, Top - y),
    };
}
