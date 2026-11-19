# Quai PDF — consignes pour Claude Code

Lis ce fichier en entier avant de toucher au code. Le propriétaire (Alexandre) parle un français décontracté : réponds-lui en français, direct et pratique. Le code, les noms de classes et les commits restent en anglais ou en français selon ce qui est le plus clair, mais sois cohérent.

## Objectif

Lecteur et éditeur de PDF **de base** pour Windows, natif, avec une interface WinUI 3 au look Windows 11. Même famille, même stack et mêmes règles que [Quai](https://github.com/memton80/quai-ssh) (client SSH), dont ce dépôt reprend l'organisation.

## Règle n°1 : ne pas réinventer la roue

Avant d'écrire une brique toi-même, cherche une bibliothèque ou un composant existant et maintenu (NuGet, GitHub, API de Windows). Si tu écris quand même ta propre version, note la raison dans `docs/DECISIONS.md` (une ligne par décision).

| Besoin | Brique |
|---|---|
| Affichage des pages | moteur PDF intégré à Windows (`Windows.Data.Pdf`). **Interdit absolu** : WebView2, PDF.js ou toute application web cachée |
| Texte (sélection, copie) | PDFium natif (`bblanchon.PDFium.Win32` / `.Linux`), API C déclarée dans `Pdf/Text/PdfiumNative.cs` |
| Modification (pages, propriétés) | `PDFsharp` **6.2.4, dernière version MIT** — à partir de 6.5.0 la licence est AGPL-3.0 : ne pas monter de version sans en parler à Alexandre |
| MVVM | `CommunityToolkit.Mvvm` |
| DI, logs | `Microsoft.Extensions.*` + `Serilog` (fichier) |
| Réglages | `System.Text.Json`, dossier `%LOCALAPPDATA%\QuaiPdf` |
| Sélecteurs de fichiers | `Microsoft.Windows.Storage.Pickers` (Windows App SDK) |
| Tests | `xUnit` |

## Stack et cible

- C#, WinUI 3 / Windows App SDK 2.6.5 (comme Quai), application **non packagée** (`WindowsPackageType=None`), self-contained.
- .NET 10 (LTS). Architectures `x64` et `arm64`.
- Interface en français d'abord (ressources `.resw`, extension `{l:Str Key=…}`).

## Structure

```
QuaiPdf.sln
src/QuaiPdf.Core/          logique sans aucune dépendance UI (opérations PDF, texte, historique, view-models, réglages)
src/QuaiPdf.App/           application WinUI 3 (vues, rendu Windows.Data.Pdf, dialogues, test de fumée)
tests/QuaiPdf.Core.Tests/  tests xUnit sur QuaiPdf.Core (tournent aussi sous Linux)
installer/QuaiPdf.iss      installateur Inno Setup
docs/DECISIONS.md          journal des choix techniques
```

`QuaiPdf.Core` ne référence jamais WinUI. Tout ce qui est testable va dans `QuaiPdf.Core`.

## Principes du document

- Le fichier d'origine n'est **jamais** modifié avant « Enregistrer ». Chaque modification produit un nouveau contenu en mémoire (`PdfOperations`), l'historique garde les contenus précédents (`PdfHistory`).
- Enregistrement atomique (fichier temporaire puis remplacement, `AtomicFile`).
- Document protégé par mot de passe ou contre la modification : **lecture seule**, on ne retire jamais une protection en silence. Mot de passe en mémoire seulement, jamais écrit ni journalisé.
- Ni contenu de document ni mot de passe dans les journaux.

## Règle CSS / HTML

Le CSS ne va **jamais** dans le HTML : fichiers séparés, pas d'attribut `style=""`, pas de balise `<style>`. (L'application n'a aucune page HTML. XAML n'est pas concerné, mais les styles vont dans `Styles/Common.xaml`, pas en ligne dans les vues.)

## UI

Fluent / Windows 11 : barre de commandes, Mica, coins arrondis, thèmes clair / sombre / système, navigation au clavier, textes d'accessibilité sur les boutons icônes. Reste sobre.

## CI et releases (GitHub Actions uniquement)

- `.github/workflows/build.yml` sur `windows-latest` : tests, publication x64 / arm64, vérification du contenu (aucun fichier web), zip, installateur, installation silencieuse, **test de fumée** sur l'application installée (`QuaiPdf.exe --smoke-test <dossier>`, captures en artefact), désinstallation complète.
- Après chaque push, vérifie le résultat du run et corrige jusqu'à ce que ce soit vert. Ne déclare rien terminé tant que le CI n'est pas vert.
- Release : automatique, comme Quai. Monter `<Version>` dans `Directory.Build.props` ; quand ce numéro arrive sur `main` sans tag, le CI crée le tag `vX.Y.Z` et publie la release.

## Façon de travailler

- Petits commits, messages clairs.
- Un choix structurant non prévu ici (changer de lib majeure, passer en MSIX, etc.) : pose la question à Alexandre avant de le faire.
- Si une action est risquée ou irréversible, préviens avant.
