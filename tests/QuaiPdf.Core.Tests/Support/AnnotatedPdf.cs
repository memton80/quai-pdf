using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace QuaiPdf.Core.Tests.Support;

/// <summary>
/// Documents d'essai pour les zones de texte : pages blanches (rotation, zone visible décalée) et annotations
/// <c>/FreeText</c> écrites comme les écrivent Firefox (PDF.js) et Acrobat.
/// </summary>
internal static class AnnotatedPdf
{
    /// <summary>Une page blanche de 300 × 400 points, tournée, avec une zone visible qui ne commence pas à 0.</summary>
    public static byte[] Blank(int rotation = 0, double cropLeft = 0, double cropBottom = 0)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.MediaBox = new PdfRectangle(new PdfSharp.Drawing.XPoint(0, 0), new PdfSharp.Drawing.XPoint(cropLeft + 300, cropBottom + 400));
        if (cropLeft != 0 || cropBottom != 0)
        {
            page.CropBox = new PdfRectangle(new PdfSharp.Drawing.XPoint(cropLeft, cropBottom), new PdfSharp.Drawing.XPoint(cropLeft + 300, cropBottom + 400));
        }

        page.Rotate = rotation;
        return Save(document);
    }

    /// <summary>
    /// Zone de texte telle que la crée l'éditeur de Firefox : <c>/DA</c> en Helvetica, <c>/Rotate</c>, apparence en
    /// Helvetica dessinant le texte. Coordonnées en points PDF.
    /// </summary>
    public static byte[] WithFirefoxFreeText(byte[] content, string text, double left, double bottom, double right, double top,
        string appearance = "/Helv 18 Tf 1 0 0 rg", int flags = 4, bool callout = false, string? richText = null)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var font = new PdfDictionary(document);
        font.Elements["/Type"] = new PdfName("/Font");
        font.Elements["/Subtype"] = new PdfName("/Type1");
        font.Elements["/BaseFont"] = new PdfName("/Helvetica");
        font.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        var fonts = new PdfDictionary(document);
        fonts.Elements["/Helv"] = font;
        var resources = new PdfDictionary(document);
        resources.Elements["/Font"] = fonts;
        var form = new PdfDictionary(document);
        form.Elements["/Type"] = new PdfName("/XObject");
        form.Elements["/Subtype"] = new PdfName("/Form");
        form.Elements["/BBox"] = new PdfArray(document, new PdfReal(0), new PdfReal(0), new PdfReal(right - left), new PdfReal(top - bottom));
        form.Elements["/Resources"] = resources;
        form.CreateStream(Encoding.ASCII.GetBytes($"BT {appearance} 1 0 0 1 2 4 Tm ({text}) Tj ET"));
        document.Internals.AddObject(form);
        var ap = new PdfDictionary(document);
        ap.Elements["/N"] = form.Reference!;

        var annotation = new PdfDictionary(document);
        annotation.Elements["/Type"] = new PdfName("/Annot");
        annotation.Elements["/Subtype"] = new PdfName("/FreeText");
        annotation.Elements["/Rect"] = new PdfArray(document, new PdfReal(left), new PdfReal(bottom), new PdfReal(right), new PdfReal(top));
        annotation.Elements["/Contents"] = new PdfString(text, PdfStringEncoding.Unicode);
        annotation.Elements["/DA"] = new PdfString(appearance);
        annotation.Elements["/F"] = new PdfInteger(flags);
        annotation.Elements["/Rotate"] = new PdfInteger(0);
        annotation.Elements["/T"] = new PdfString("Firefox");
        annotation.Elements["/NM"] = new PdfString("pdfjs_internal_editor_0");
        annotation.Elements["/AP"] = ap;
        if (callout)
        {
            annotation.Elements["/IT"] = new PdfName("/FreeTextCallout");
            annotation.Elements["/CL"] = new PdfArray(document, new PdfReal(left - 50), new PdfReal(bottom - 50), new PdfReal(left), new PdfReal(bottom));
        }

        if (richText is not null)
        {
            annotation.Elements["/RC"] = new PdfString(richText);
            annotation.Elements["/DS"] = new PdfString("font: Helvetica,sans-serif 18.0pt; color:#FF0000");
        }

        document.Internals.AddObject(annotation);
        var annots = page.Elements.GetArray("/Annots") ?? new PdfArray(document);
        annots.Elements.Add(annotation.Reference!);
        page.Elements["/Annots"] = annots;
        return Save(document);
    }

    /// <summary>Ajoute une bulle (<c>/Popup</c>) et une réponse (<c>/IRT</c>) à la première annotation de la page.</summary>
    public static byte[] WithPopupAndReply(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var annots = page.Elements.GetArray("/Annots")!;
        var parent = (PdfDictionary)((PdfReference)annots.Elements[0]).Value;

        var popup = new PdfDictionary(document);
        popup.Elements["/Type"] = new PdfName("/Annot");
        popup.Elements["/Subtype"] = new PdfName("/Popup");
        popup.Elements["/Rect"] = new PdfArray(document, new PdfReal(200), new PdfReal(300), new PdfReal(280), new PdfReal(380));
        popup.Elements["/Parent"] = parent.Reference!;
        document.Internals.AddObject(popup);
        parent.Elements["/Popup"] = popup.Reference!;

        var reply = new PdfDictionary(document);
        reply.Elements["/Type"] = new PdfName("/Annot");
        reply.Elements["/Subtype"] = new PdfName("/Text");
        reply.Elements["/Rect"] = new PdfArray(document, new PdfReal(10), new PdfReal(10), new PdfReal(30), new PdfReal(30));
        reply.Elements["/Contents"] = new PdfString("Réponse", PdfStringEncoding.Unicode);
        reply.Elements["/IRT"] = parent.Reference!;
        document.Internals.AddObject(reply);

        var other = new PdfDictionary(document);
        other.Elements["/Type"] = new PdfName("/Annot");
        other.Elements["/Subtype"] = new PdfName("/Square");
        other.Elements["/Rect"] = new PdfArray(document, new PdfReal(50), new PdfReal(50), new PdfReal(60), new PdfReal(60));
        document.Internals.AddObject(other);

        annots.Elements.Add(popup.Reference!);
        annots.Elements.Add(reply.Reference!);
        annots.Elements.Add(other.Reference!);
        return Save(document);
    }

    /// <summary>Sous-types des annotations de la première page, dans l'ordre de <c>/Annots</c>.</summary>
    public static IReadOnlyList<string> Subtypes(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        return annots is null
            ? []
            : [.. annots.Elements.Select(item => (item is PdfReference r ? r.Value : item) as PdfDictionary)
                .Select(d => d?.Elements.GetName("/Subtype") ?? "?")];
    }

    /// <summary>Dictionnaire de la n-ième annotation de la page.</summary>
    public static T Read<T>(byte[] content, int page, int index, Func<PdfDictionary, T> read)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var item = document.Pages[page].Elements.GetArray("/Annots")!.Elements[index];
        return read((PdfDictionary)(item is PdfReference r ? r.Value : item));
    }

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }
}
