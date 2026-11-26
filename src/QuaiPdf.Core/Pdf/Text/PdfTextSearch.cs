using System.Globalization;

namespace QuaiPdf.Core.Pdf.Text;

/// <summary>Une occurrence trouvée sur une page : caractères <see cref="Start"/> à <see cref="Start"/> + <see cref="Length"/> (exclu).</summary>
public readonly record struct PdfTextMatch(int Start, int Length);

/// <summary>
/// Recherche dans le texte d'une page, comme dans un navigateur : sans tenir compte des majuscules ni des accents
/// (« ecole » trouve « École »), selon les règles de la langue (comparaison de .NET, <see cref="CompareInfo"/>).
/// Les fins de ligne du texte valent une espace : une expression coupée en fin de ligne est trouvée.
/// </summary>
public static class PdfTextSearch
{
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    /// <summary>Occurrences de <paramref name="query"/> dans <paramref name="text"/>, dans l'ordre, sans chevauchement.</summary>
    public static IReadOnlyList<PdfTextMatch> Find(string text, string query, int max = int.MaxValue, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        var needle = Normalize(query.Trim());
        if (needle.Length == 0 || text.Length == 0 || max <= 0)
        {
            return [];
        }

        var haystack = Normalize(text);
        var compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        var matches = new List<PdfTextMatch>();
        var start = 0;
        while (start < haystack.Length && matches.Count < max)
        {
            var index = compare.IndexOf(haystack.AsSpan(start), needle, Options, out var length);
            if (index < 0)
            {
                break;
            }

            index += start;
            if (length == 0)
            {
                // Caractère ignorable (accent seul…) : on avance d'un cran pour ne pas boucler.
                start = index + 1;
                continue;
            }

            matches.Add(new PdfTextMatch(index, length));
            start = index + length;
        }

        return matches;
    }

    /// <summary>
    /// Extrait autour d'une occurrence, sur une ligne : jusqu'à <paramref name="context"/> caractères avant et après,
    /// coupés à un début ou une fin de mot.
    /// </summary>
    public static (string Before, string Match, string After) Excerpt(string text, PdfTextMatch match, int context = 40)
    {
        ArgumentNullException.ThrowIfNull(text);
        var from = Math.Max(0, match.Start - context);
        var to = Math.Min(text.Length, match.Start + match.Length + context);
        var before = text[from..match.Start];
        var after = text[(match.Start + match.Length)..to];

        if (from > 0 && before.IndexOfAny([' ', '\r', '\n']) is var space and >= 0)
        {
            before = "…" + before[(space + 1)..];
        }

        if (to < text.Length && after.LastIndexOfAny([' ', '\r', '\n']) is var end and > 0)
        {
            after = after[..end] + "…";
        }

        return (OneLine(before).TrimStart(), OneLine(text.Substring(match.Start, match.Length)), OneLine(after).TrimEnd());
    }

    // Même longueur que le texte d'origine : les positions trouvées restent celles des caractères de PDFium. Une fin de
    // ligne « \r\n » devient une espace suivie d'un trait d'union conditionnel, ignoré par la comparaison : elle vaut
    // une seule espace.
    private static string Normalize(string text) =>
        text.Replace("\r\n", " \u00AD").Replace('\r', ' ').Replace('\n', ' ').Replace('\u00A0', ' ');

    private static string OneLine(string text) => text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
}
