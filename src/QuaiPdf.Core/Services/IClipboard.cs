namespace QuaiPdf.Core.Services;

/// <summary>Presse-papiers de Windows (implémenté par l'application).</summary>
public interface IClipboard
{
    void SetText(string text);
}
