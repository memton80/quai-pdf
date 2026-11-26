using System.Text;
using PdfSharp.Pdf;
using QuaiPdf.Core.Pdf.Text;
using static QuaiPdf.Core.Pdf.PdfTextBoxes;

namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Un surlignage du document (annotation <c>/Highlight</c>), qu'il vienne de Quai PDF, de Firefox, de Chrome, d'Edge ou
/// d'Acrobat. <see cref="Index"/> est sa place dans la liste <c>/Annots</c> de la page.
/// </summary>
/// <param name="Areas">Rectangles surlignés (un par morceau de ligne), en pixels à 100 %, repère de la page affichée.</param>
/// <param name="CanEdit">Sa couleur peut changer et il peut être supprimé (son auteur ne l'a pas verrouillé).</param>
public sealed record PdfHighlight(int Page, int Index, IReadOnlyList<PdfRect> Areas, bool CanEdit)
{
    public bool Contains(PdfPoint point) => Areas.Any(area => area.Contains(point));
}

/// <summary>Rectangles à surligner sur une page (pixels à 100 %, comme la sélection de texte affichée).</summary>
public sealed record PdfHighlightSpan(int Page, IReadOnlyList<PdfRect> Areas);

/// <summary>
/// Surlignage du texte, comme au marqueur sur du papier (annotations <c>/Highlight</c>, ISO 32000-1, 12.5.6.10) : ajout,
/// changement de couleur et suppression, avec PDFsharp, comme les autres opérations.
/// <para>
/// Chaque surlignage a toujours une apparence (<c>/AP</c>) : des aplats de couleur en mode de fusion « produit »
/// (<c>/Multiply</c>), qui teintent le papier sans couvrir l'encre, exactement comme un surligneur ; c'est aussi ce
/// qu'écrivent Acrobat, Firefox et Chrome. L'apparence est dans le repère du PDF (matrice identité), elle suit donc
/// les rotations de page sans rien faire. Les rectangles sont aussi gardés dans <c>/QuadPoints</c>, dans le sens du
/// texte (haut gauche, haut droite, bas gauche, bas droite, l'ordre qu'utilisent tous les lecteurs).
/// </para>
/// </summary>
public static class PdfHighlights
{
    private const string BlendState = "/GS0";

    /// <summary>Ajoute un surlignage par page (pages et rectangles comme à l'écran) ; une page sans rectangle est ignorée.</summary>
    /// <exception cref="ArgumentException">Aucun rectangle à surligner.</exception>
    public static byte[] Add(byte[] content, IReadOnlyList<PdfHighlightSpan> spans, PdfColor color)
    {
        ArgumentNullException.ThrowIfNull(spans);
        var useful = spans.Where(s => s.Areas.Any(a => a.Width > 0 && a.Height > 0)).ToList();
        if (useful.Count == 0)
        {
            throw new ArgumentException("Rien à surligner.", nameof(spans));
        }

        return PdfOperations.Transform(content, document =>
        {
            foreach (var span in useful)
            {
                var pdfPage = PageAt(document, span.Page);
                var geometry = PageGeometry.Of(pdfPage);
                var quads = span.Areas.Where(a => a.Width > 0 && a.Height > 0).Select(a => Quad(geometry, a)).ToList();

                var annotation = new PdfDictionary(document);
                annotation.Elements["/Type"] = new PdfName("/Annot");
                annotation.Elements["/Subtype"] = new PdfName("/Highlight");
                annotation.Elements["/NM"] = new PdfString($"quaipdf-{Guid.NewGuid():N}");
                annotation.Elements["/CreationDate"] = new PdfString(DateString());
                annotation.Elements["/F"] = new PdfInteger(FlagPrint);
                annotation.Elements["/QuadPoints"] = Numbers(document, [.. quads.SelectMany(q => q)]);
                if (pdfPage.Reference is { } pageReference)
                {
                    annotation.Elements["/P"] = pageReference;
                }

                Paint(document, annotation, quads, color);
                document.Internals.AddObject(annotation);
                Annotations(document, pdfPage, create: true)!.Elements.Add(annotation.Reference!);
            }
        });
    }

    /// <summary>Change la couleur d'un surlignage : son apparence est refaite d'après ses <c>/QuadPoints</c>.</summary>
    /// <exception cref="InvalidOperationException">Ce n'est pas un surlignage, ou son auteur l'a verrouillé.</exception>
    public static byte[] SetColor(byte[] content, int page, int index, PdfColor color)
    {
        return PdfOperations.Transform(content, document =>
        {
            var pdfPage = PageAt(document, page);
            var annotation = Editable(HighlightAt(document, pdfPage, index));
            Paint(document, annotation, Quads(annotation), color);
        });
    }

    /// <summary>Supprime un surlignage, avec sa bulle (<c>/Popup</c>) et les réponses qui s'y rattachent (<c>/IRT</c>).</summary>
    /// <exception cref="InvalidOperationException">Ce n'est pas un surlignage, ou son auteur l'a verrouillé.</exception>
    public static byte[] Delete(byte[] content, int page, int index)
    {
        return PdfOperations.Transform(content, document =>
        {
            var pdfPage = PageAt(document, page);
            Remove(document, pdfPage, Editable(HighlightAt(document, pdfPage, index)));
        });
    }

    /// <summary>Couleur, rectangle englobant et apparence ; les autres propriétés (auteur, commentaire…) sont gardées.</summary>
    private static void Paint(PdfDocument document, PdfDictionary annotation, IReadOnlyList<double[]> quads, PdfColor color)
    {
        var xs = quads.SelectMany(q => new[] { q[0], q[2], q[4], q[6] }).ToList();
        var ys = quads.SelectMany(q => new[] { q[1], q[3], q[5], q[7] }).ToList();
        var (left, bottom, right, top) = (xs.Min(), ys.Min(), xs.Max(), ys.Max());

        annotation.Elements["/Rect"] = Numbers(document, left, bottom, right, top);
        annotation.Elements["/C"] = Numbers(document, color.R, color.G, color.B);
        annotation.Elements["/M"] = new PdfString(DateString());

        var blend = new PdfDictionary(document);
        blend.Elements["/Type"] = new PdfName("/ExtGState");
        blend.Elements["/BM"] = new PdfName("/Multiply");
        var states = new PdfDictionary(document);
        states.Elements[BlendState] = blend;
        var resources = new PdfDictionary(document);
        resources.Elements["/ExtGState"] = states;

        var form = new PdfDictionary(document);
        form.Elements["/Type"] = new PdfName("/XObject");
        form.Elements["/Subtype"] = new PdfName("/Form");
        form.Elements["/FormType"] = new PdfInteger(1);
        form.Elements["/BBox"] = Numbers(document, left, bottom, right, top);
        form.Elements["/Matrix"] = Numbers(document, 1, 0, 0, 1, 0, 0);
        form.Elements["/Resources"] = resources;
        form.CreateStream(ContentStream(quads, color));
        document.Internals.AddObject(form);

        var appearance = new PdfDictionary(document);
        appearance.Elements["/N"] = form.Reference!;
        annotation.Elements["/AP"] = appearance;
        annotation.Elements.Remove("/AS");
    }

    private static byte[] ContentStream(IReadOnlyList<double[]> quads, PdfColor color)
    {
        var builder = new StringBuilder();
        builder.Append(Invariant($"{BlendState} gs\n{color.R:0.###} {color.G:0.###} {color.B:0.###} rg\n"));
        foreach (var q in quads)
        {
            // Haut gauche, haut droite, bas droite, bas gauche.
            builder.Append(Invariant($"{q[0]:0.###} {q[1]:0.###} m {q[2]:0.###} {q[3]:0.###} l {q[6]:0.###} {q[7]:0.###} l {q[4]:0.###} {q[5]:0.###} l h\n"));
        }

        builder.Append("f\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>Rectangle affiché vers un quadrilatère du PDF, dans le sens du texte vu à l'écran.</summary>
    private static double[] Quad(PageGeometry geometry, PdfRect area)
    {
        var x = area.X / PdfPageSize.PixelsPerPoint;
        var y = area.Y / PdfPageSize.PixelsPerPoint;
        var right = area.Right / PdfPageSize.PixelsPerPoint;
        var bottom = area.Bottom / PdfPageSize.PixelsPerPoint;
        var (x1, y1) = geometry.ToUser(x, y);
        var (x2, y2) = geometry.ToUser(right, y);
        var (x3, y3) = geometry.ToUser(x, bottom);
        var (x4, y4) = geometry.ToUser(right, bottom);
        return [x1, y1, x2, y2, x3, y3, x4, y4];
    }

    /// <summary>Quadrilatères d'un surlignage existant ; à défaut, son rectangle.</summary>
    private static List<double[]> Quads(PdfDictionary annotation)
    {
        var quads = new List<double[]>();
        if (Resolve(annotation.Elements["/QuadPoints"]) is PdfArray points)
        {
            for (var i = 0; i + 8 <= points.Elements.Count; i += 8)
            {
                quads.Add([.. Enumerable.Range(i, 8).Select(points.Elements.GetReal)]);
            }
        }

        if (quads.Count == 0 && Resolve(annotation.Elements["/Rect"]) is PdfArray { Elements.Count: 4 } rect)
        {
            var (l, b, r, t) = (rect.Elements.GetReal(0), rect.Elements.GetReal(1), rect.Elements.GetReal(2), rect.Elements.GetReal(3));
            quads.Add([l, t, r, t, l, b, r, b]);
        }

        return quads.Count > 0 ? quads : throw new InvalidOperationException("Ce surlignage n'a pas de forme lisible.");
    }

    private static PdfDictionary HighlightAt(PdfDocument document, PdfPage page, int index)
    {
        var annotations = Annotations(document, page, create: false);
        if (annotations is null || index < 0 || index >= annotations.Elements.Count
            || Resolve(annotations.Elements[index]) is not PdfDictionary annotation
            || annotation.Elements.GetName("/Subtype") != "/Highlight")
        {
            throw new InvalidOperationException("Ce surlignage n'existe plus dans le document.");
        }

        return annotation;
    }

    private static PdfDictionary Editable(PdfDictionary annotation) =>
        (annotation.Elements.GetInteger("/F") & (FlagReadOnly | FlagLocked)) != 0
            ? throw new InvalidOperationException("L'auteur de ce surlignage l'a verrouillé.")
            : annotation;
}
