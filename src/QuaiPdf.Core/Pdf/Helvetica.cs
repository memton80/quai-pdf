namespace QuaiPdf.Core.Pdf;

/// <summary>
/// Helvetica, l'une des 14 polices standard du PDF (aucun fichier de police à intégrer, connue de tous les lecteurs),
/// avec le codage WinAnsi : de quoi écrire l'apparence d'une zone de texte comme le fait Firefox. Les largeurs viennent
/// des métriques publiées par Adobe (Helvetica.afm, relevées dans les tables de ReportLab) ; le codage est celui de
/// Windows-1252 (WinAnsiEncoding, ISO 32000-1, annexe D).
/// </summary>
internal static class Helvetica
{
    /// <summary>Hauteur au-dessus de la ligne de base et profondeur en dessous, en millièmes de la taille.</summary>
    public const double Ascent = 718;

    public const double Descent = 207;

    // Largeur de chaque code WinAnsi, en millièmes de la taille (0 : code sans caractère).
    private static readonly int[] Widths =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 0,
        556, 0, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 0, 611, 0,
        0, 222, 222, 333, 333, 350, 556, 1000, 333, 1000, 500, 333, 944, 0, 500, 667,
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500,
    ];

    // Codes 0x80 à 0x9F : là où Windows-1252 diffère de Latin-1.
    private static readonly Dictionary<char, byte> Windows1252 = new()
    {
        ['\u20AC'] = 0x80, ['\u201A'] = 0x82, ['\u0192'] = 0x83, ['\u201E'] = 0x84, ['\u2026'] = 0x85, ['\u2020'] = 0x86,
        ['\u2021'] = 0x87, ['\u02C6'] = 0x88, ['\u2030'] = 0x89, ['\u0160'] = 0x8A, ['\u2039'] = 0x8B, ['\u0152'] = 0x8C,
        ['\u017D'] = 0x8E, ['\u2018'] = 0x91, ['\u2019'] = 0x92, ['\u201C'] = 0x93, ['\u201D'] = 0x94, ['\u2022'] = 0x95,
        ['\u2013'] = 0x96, ['\u2014'] = 0x97, ['\u02DC'] = 0x98, ['\u2122'] = 0x99, ['\u0161'] = 0x9A, ['\u203A'] = 0x9B,
        ['\u0153'] = 0x9C, ['\u017E'] = 0x9E, ['\u0178'] = 0x9F,
    };

    /// <summary>Caractère remplaçant ceux que Helvetica (WinAnsi) ne sait pas écrire.</summary>
    public const byte Replacement = (byte)'?';

    /// <summary>Code WinAnsi d'un caractère, ou <c>null</c> s'il n'en a pas.</summary>
    public static byte? Encode(char c)
    {
        if (c == '\t')
        {
            return (byte)' ';
        }

        if (c is (>= ' ' and <= '~') or (>= '\u00A0' and <= '\u00FF'))
        {
            return (byte)c;
        }

        return Windows1252.TryGetValue(c, out var code) ? code : null;
    }

    /// <summary>Le caractère s'écrit tel quel dans l'apparence (sinon il y devient un « ? »).</summary>
    public static bool CanEncode(char c) => Encode(c) is not null;

    /// <summary>Texte codé en WinAnsi ; les caractères impossibles deviennent <see cref="Replacement"/>.</summary>
    public static byte[] EncodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = Encode(text[i]) ?? Replacement;
        }

        return bytes;
    }

    /// <summary>Largeur d'un texte, en points, à cette taille de police.</summary>
    public static double Width(string text, double fontSize) =>
        EncodeText(text).Sum(code => Widths[code]) * fontSize / 1000;
}
