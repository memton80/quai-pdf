# Développement

## Organisation

| Dossier | Contenu |
|---|---|
| `src/QuaiPdf.Core` | sans interface : opérations PDF (`Pdf/PdfOperations.cs`, PDFsharp), historique, view-models (`ViewModels/DocumentViewModel.cs`), réglages |
| `src/QuaiPdf.App` | WinUI 3 : fenêtre, vue du document, rendu des pages (`Services/WindowsPdfRenderer.cs`, `Windows.Data.Pdf`), dialogues, test de fumée |
| `tests/QuaiPdf.Core.Tests` | xUnit : opérations sur de vrais PDF fabriqués avec PDFsharp, view-models avec un moteur d'affichage factice, clés de traduction |
| `installer/QuaiPdf.iss` | installateur Inno Setup (par utilisateur, « Ouvrir avec » pour les PDF) |
| `tools/make_icon.py` | génère l'icône (Pillow) |

`QuaiPdf.Core` ne dépend pas de Windows : ses tests tournent aussi sous Linux.

## Commandes

```
dotnet test --project tests/QuaiPdf.Core.Tests/QuaiPdf.Core.Tests.csproj
dotnet build src/QuaiPdf.App/QuaiPdf.App.csproj -p:Platform=x64        (Windows seulement)
dotnet publish src/QuaiPdf.App/QuaiPdf.App.csproj -c Release -r win-x64 -p:Platform=x64 -o publish/x64
publish\x64\QuaiPdf.exe --smoke-test %TEMP%\quaipdf-smoke
```

## Ajouter un texte à l'interface

Toutes les chaînes sont dans `src/QuaiPdf.App/Strings/fr-FR/Resources.resw`, utilisées par `{l:Str Key=…}` dans le XAML, `Loc.Get` dans l'application et `IStrings` dans les view-models. Un test vérifie que chaque clé utilisée existe.

## Publier une version

Monter `<Version>` dans `Directory.Build.props` et pousser sur `main` : le CI construit, teste, crée le tag `vX.Y.Z` et publie la release (installateurs et zips x64 / arm64).
