using System.Text.Json;
using System.Text.Json.Serialization;
using QuaiPdf.Core.Models;

namespace QuaiPdf.Core.Storage;

/// <summary>
/// Sérialisation des fichiers JSON de Quai PDF, générée à la compilation : aucune réflexion, ce qui permet de réduire
/// l'application à la publication (trimming). Propriétés et énumérations en camelCase, indentation, commentaires et
/// virgules finales tolérés.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(CamelCaseEnumConverter<AppTheme>)])]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class QuaiPdfJsonContext : JsonSerializerContext;

/// <summary>Énumération écrite en texte camelCase (<c>"dark"</c>) ; les nombres restent acceptés en lecture.</summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;
