using System.Globalization;
using QuaiPdf.Core.Pdf.Text;

namespace QuaiPdf.Core.Pdf;

/// <summary>Couleur RVB d'un texte (composantes de 0 à 1).</summary>
public readonly record struct PdfColor(double R, double G, double B)
{
    public static PdfColor Black { get; } = new(0, 0, 0);

    /// <summary>« #RRGGBB », comme dans le style par défaut (<c>/DS</c>) d'Acrobat.</summary>
    public string ToHex() =>
        string.Create(CultureInfo.InvariantCulture, $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}");

    public static PdfColor FromBytes(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    private static int Byte(double component) => (int)Math.Round(Math.Clamp(component, 0, 1) * 255);
}

/// <summary>
/// Une zone de texte du document (annotation <c>/FreeText</c>), qu'elle vienne de Quai PDF, de Firefox, de Chrome, d'Edge
/// ou d'Acrobat. <see cref="Index"/> est sa place dans la liste <c>/Annots</c> de la page : c'est ainsi que les
/// modifications la retrouvent, dans le contenu même où elle a été lue.
/// </summary>
/// <param name="Area">Rectangle affiché, en pixels à 100 % (origine en haut à gauche de la page affichée).</param>
/// <param name="FontSize">Taille du texte, en points.</param>
public sealed record PdfTextBox(
    int Page,
    int Index,
    PdfRect Area,
    string Text,
    double FontSize,
    PdfColor Color,
    bool CanEditText,
    bool CanMove,
    bool CanDelete);

/// <summary>Taille et couleur du texte d'une zone, lues dans son apparence par défaut (<c>/DA</c>).</summary>
public readonly record struct PdfTextStyle(double FontSize, PdfColor Color)
{
    public const double MinFontSize = 4;
    public const double MaxFontSize = 96;
    public const double DefaultFontSize = 12;

    public static PdfTextStyle Default { get; } = new(DefaultFontSize, PdfColor.Black);

    /// <summary>
    /// Lit une chaîne d'apparence par défaut (<c>/Helv 12 Tf 0 0 1 rg</c>) : la taille après <c>Tf</c>, la couleur de
    /// remplissage (<c>g</c>, <c>rg</c> ou <c>k</c>). Ce qui manque ou ne se lit pas garde la valeur par défaut.
    /// </summary>
    public static PdfTextStyle Parse(string? appearance)
    {
        var size = DefaultFontSize;
        var color = PdfColor.Black;
        if (string.IsNullOrWhiteSpace(appearance))
        {
            return new PdfTextStyle(size, color);
        }

        var tokens = appearance.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "Tf" when i >= 1 && Number(tokens[i - 1]) is { } tf && tf > 0:
                    size = tf;
                    break;
                case "g" when i >= 1 && Number(tokens[i - 1]) is { } gray:
                    color = new PdfColor(gray, gray, gray);
                    break;
                case "rg" when i >= 3 && Number(tokens[i - 3]) is { } r && Number(tokens[i - 2]) is { } g && Number(tokens[i - 1]) is { } b:
                    color = new PdfColor(r, g, b);
                    break;
                case "k" when i >= 4 && Number(tokens[i - 4]) is { } c && Number(tokens[i - 3]) is { } m
                                     && Number(tokens[i - 2]) is { } y && Number(tokens[i - 1]) is { } k:
                    color = new PdfColor((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k));
                    break;
            }
        }

        return new PdfTextStyle(
            Math.Clamp(size, MinFontSize, MaxFontSize),
            new PdfColor(Math.Clamp(color.R, 0, 1), Math.Clamp(color.G, 0, 1), Math.Clamp(color.B, 0, 1)));

        static double? Number(string token) =>
            double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }
}
