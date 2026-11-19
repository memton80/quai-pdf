using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Modifications de documents PDF avec PDFsharp. Chaque opération part d'un contenu (octets) et en produit un
/// nouveau, sans toucher au fichier d'origine : l'historique (annuler / rétablir) se résume à garder les contenus
/// successifs, et l'enregistrement n'écrit sur le disque que ce qui est affiché.
/// Les indices de page commencent à 0.
/// </summary>
public static class PdfOperations
{
    /// <summary>Indique si le contenu peut être modifié (sans mot de passe).</summary>
    public static PdfEditability Inspect(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var document = Open(content, PdfDocumentOpenMode.Modify);
            return document.PageCount > 0 ? PdfEditability.Editable : PdfEditability.Unsupported;
        }
        catch (PdfFileException ex) when (ex.Problem == PdfProblem.Protected)
        {
            return PdfEditability.Protected;
        }
        catch (PdfFileException)
        {
            return PdfEditability.Unsupported;
        }
    }

    public static int PageCount(byte[] content)
    {
        using var document = Open(content, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>Taille affichée de chaque page (zone visible, rotation comprise).</summary>
    public static IReadOnlyList<PdfPageSize> PageSizes(byte[] content)
    {
        using var document = Open(content, PdfDocumentOpenMode.Import);
        var sizes = new List<PdfPageSize>(document.PageCount);
        foreach (var page in document.Pages)
        {
            var box = page.Elements.GetRectangle("/CropBox");
            if (box.IsZero)
            {
                box = page.MediaBox;
            }

            var quarterTurn = NormalizeRotation(page.Rotate) % 180 != 0;
            sizes.Add(quarterTurn
                ? PdfPageSize.FromPoints(box.Height, box.Width)
                : PdfPageSize.FromPoints(box.Width, box.Height));
        }

        return sizes;
    }

    /// <summary>Rotation de chaque page, en degrés (0, 90, 180 ou 270).</summary>
    public static IReadOnlyList<int> PageRotations(byte[] content)
    {
        using var document = Open(content, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>().Select(p => NormalizeRotation(p.Rotate)).ToList();
    }

    /// <summary>Tourne les pages indiquées de <paramref name="degrees"/> (multiple de 90, positif = sens horaire).</summary>
    public static byte[] Rotate(byte[] content, IEnumerable<int> pages, int degrees)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (degrees % 90 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "La rotation doit être un multiple de 90°.");
        }

        return Transform(content, document =>
        {
            foreach (var index in Distinct(pages, document.PageCount))
            {
                var page = document.Pages[index];
                page.Rotate = NormalizeRotation(page.Rotate + degrees);
            }
        });
    }

    /// <summary>Supprime les pages indiquées ; il doit en rester au moins une.</summary>
    public static byte[] Delete(byte[] content, IEnumerable<int> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return Transform(content, document =>
        {
            var indices = Distinct(pages, document.PageCount);
            if (indices.Count >= document.PageCount)
            {
                throw new InvalidOperationException("Un document doit garder au moins une page.");
            }

            foreach (var index in indices.OrderDescending())
            {
                document.Pages.RemoveAt(index);
            }
        });
    }

    /// <summary>
    /// Remet les pages dans un nouvel ordre : <paramref name="order"/> donne, pour chaque position, l'indice de la
    /// page d'origine (une permutation de 0..n-1).
    /// </summary>
    public static byte[] Reorder(byte[] content, IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return Transform(content, document =>
        {
            var count = document.PageCount;
            if (order.Count != count || order.Distinct().Count() != count || order.Any(i => i < 0 || i >= count))
            {
                throw new ArgumentException("L'ordre doit contenir chaque page une seule fois.", nameof(order));
            }

            var original = document.Pages.Cast<PdfPage>().ToList();
            for (var position = 0; position < count; position++)
            {
                var wanted = original[order[position]];
                var current = IndexOf(document, wanted, position);
                if (current != position)
                {
                    document.Pages.MovePage(current, position);
                }
            }
        });
    }

    /// <summary>Déplace les pages indiquées d'un cran vers le début (<paramref name="offset"/> = -1) ou la fin (+1).</summary>
    public static IReadOnlyList<int> MoveOrder(int pageCount, IEnumerable<int> pages, int offset)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Déplacement d'un cran seulement.");
        }

        var order = Enumerable.Range(0, pageCount).ToList();
        var selected = new HashSet<int>(pages.Where(i => i >= 0 && i < pageCount));
        var positions = offset < 0 ? Enumerable.Range(1, Math.Max(0, pageCount - 1)) : Enumerable.Range(0, Math.Max(0, pageCount - 1)).Reverse();
        foreach (var position in positions)
        {
            var neighbour = position + offset;
            // Une page sélectionnée passe devant (ou derrière) sa voisine, sauf si celle-ci est aussi sélectionnée :
            // un bloc de pages se déplace d'un seul tenant et s'arrête au bord du document.
            if (selected.Contains(order[position]) && !selected.Contains(order[neighbour]))
            {
                (order[position], order[neighbour]) = (order[neighbour], order[position]);
            }
        }

        return order;
    }

    /// <summary>Insère toutes les pages de <paramref name="sources"/> (dans l'ordre) à partir de la position <paramref name="index"/>.</summary>
    public static byte[] Insert(byte[] content, int index, IEnumerable<byte[]> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var imports = sources.Select(source => Open(source, PdfDocumentOpenMode.Import)).ToList();
        try
        {
            return Transform(content, document =>
            {
                var position = Math.Clamp(index, 0, document.PageCount);
                foreach (var import in imports)
                {
                    foreach (var page in import.Pages)
                    {
                        document.Pages.Insert(position++, page);
                    }
                }
            });
        }
        finally
        {
            foreach (var import in imports)
            {
                import.Dispose();
            }
        }
    }

    /// <summary>Nouveau document fait des pages indiquées, dans l'ordre donné.</summary>
    public static byte[] Extract(byte[] content, IEnumerable<int> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        using var source = Open(content, PdfDocumentOpenMode.Import);
        var indices = pages.Where(i => i >= 0 && i < source.PageCount).Distinct().ToList();
        if (indices.Count == 0)
        {
            throw new ArgumentException("Aucune page à extraire.", nameof(pages));
        }

        using var result = new PdfDocument();
        result.Version = Math.Max(result.Version, source.Version);
        foreach (var index in indices)
        {
            result.AddPage(source.Pages[index]);
        }

        CopyMetadata(source, result);
        return Save(result);
    }

    public static PdfMetadata ReadMetadata(byte[] content)
    {
        using var document = Open(content, PdfDocumentOpenMode.Import);
        var info = document.Info;
        return new PdfMetadata(info.Title ?? string.Empty, info.Author ?? string.Empty, info.Subject ?? string.Empty, info.Keywords ?? string.Empty);
    }

    public static byte[] WriteMetadata(byte[] content, PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Transform(content, document =>
        {
            document.Info.Title = metadata.Title.Trim();
            document.Info.Author = metadata.Author.Trim();
            document.Info.Subject = metadata.Subject.Trim();
            document.Info.Keywords = metadata.Keywords.Trim();
        });
    }

    internal static int NormalizeRotation(int degrees) => ((degrees % 360) + 360) % 360;

    private static byte[] Transform(byte[] content, Action<PdfDocument> change)
    {
        using var document = Open(content, PdfDocumentOpenMode.Modify);
        change(document);
        return Save(document);
    }

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Ouvre un contenu sans jamais fournir de mot de passe : un fichier protégé lève <see cref="PdfProblem.Protected"/>.</summary>
    private static PdfDocument Open(byte[] content, PdfDocumentOpenMode mode)
    {
        ArgumentNullException.ThrowIfNull(content);
        var passwordRequested = false;
        PdfDocument? document;
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            document = PdfReader.Open(stream, mode, args =>
            {
                passwordRequested = true;
                args.Abort = true;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw passwordRequested ? Protected(ex) : new PdfFileException(PdfProblem.Unreadable, "Document PDF illisible.", ex);
        }

        // Demande de mot de passe abandonnée : PDFsharp rend un document nul plutôt que de lever une exception.
        return document ?? throw (passwordRequested ? Protected(null) : new PdfFileException(PdfProblem.Unreadable, "Document PDF illisible."));

        static PdfFileException Protected(Exception? inner) =>
            new(PdfProblem.Protected, "Document protégé par un mot de passe.", inner);
    }

    private static List<int> Distinct(IEnumerable<int> pages, int count)
    {
        var indices = pages.Distinct().ToList();
        if (indices.Any(i => i < 0 || i >= count))
        {
            throw new ArgumentOutOfRangeException(nameof(pages), "Numéro de page hors du document.");
        }

        return indices;
    }

    private static int IndexOf(PdfDocument document, PdfPage page, int start)
    {
        for (var i = start; i < document.PageCount; i++)
        {
            if (ReferenceEquals(document.Pages[i], page))
            {
                return i;
            }
        }

        throw new InvalidOperationException("Page introuvable pendant le réordonnancement.");
    }

    private static void CopyMetadata(PdfDocument source, PdfDocument target)
    {
        target.Info.Title = source.Info.Title;
        target.Info.Author = source.Info.Author;
        target.Info.Subject = source.Info.Subject;
        target.Info.Keywords = source.Info.Keywords;
    }
}
