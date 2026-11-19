using QuaiPdf.App.Localization;
using QuaiPdf.Core.Localization;

namespace QuaiPdf.App.Services;

/// <summary>Chaînes des view-models, servies par les ressources <c>.resw</c>.</summary>
internal sealed class ReswStrings : IStrings
{
    public string Get(string key) => Loc.Get(key);

    public string Format(string key, params object?[] args) => Loc.Format(key, args);
}
