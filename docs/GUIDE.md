# Guide de Quai PDF

## Ouvrir un document

- Bouton **Ouvrir** (`Ctrl+O`), ou glissez un PDF dans la fenêtre depuis l'Explorateur.
- Clic droit sur un PDF dans l'Explorateur, **Ouvrir avec**, **Quai PDF**.
- Un document protégé par un mot de passe le demande à l'ouverture. Le mot de passe n'est jamais enregistré.

## Lire

| Action | Comment |
|---|---|
| Page suivante / précédente | flèches de la barre d'outils, molette, `Pg.Suiv` / `Pg.Préc` |
| Aller à une page | tapez son numéro dans la barre d'outils |
| Zoom avant / arrière | `Ctrl+Plus` / `Ctrl+Moins`, `Ctrl+molette`, menu du zoom |
| Taille réelle | `Ctrl+0` |
| Ajuster à la largeur | menu du zoom |
| Afficher / masquer les vignettes | bouton **Vignettes** |

## Modifier

Les modifications s'appliquent aux **pages sélectionnées dans les vignettes** (clic, `Ctrl+clic`, `Maj+clic`), ou à la page affichée si rien n'est sélectionné. `Échap` dans les vignettes vide la sélection.

| Action | Comment |
|---|---|
| Pivoter à droite / à gauche | `Ctrl+R` / `Ctrl+Maj+R` |
| Changer l'ordre | faites glisser les vignettes, ou **Monter** / **Descendre** |
| Supprimer | `Suppr` dans les vignettes, ou **Supprimer** (une page au moins doit rester) |
| Insérer les pages d'autres PDF | `Ctrl+I` : elles arrivent après la page en cours |
| Extraire des pages dans un nouveau PDF | `Ctrl+E` |
| Propriétés (titre, auteur…) | `Ctrl+D` |
| Annuler / rétablir | `Ctrl+Z` / `Ctrl+Y` (30 étapes) |
| Enregistrer / enregistrer sous | `Ctrl+S` / `Ctrl+Maj+S` |
| Fermer le document | `Ctrl+W` |

Rien n'est écrit sur le disque avant **Enregistrer**. Un astérisque devant le nom, dans la barre de titre, signale des modifications non enregistrées ; Quai PDF les propose à l'enregistrement avant de fermer le document ou la fenêtre.

## Lecture seule

Certains documents s'affichent sans pouvoir être modifiés (un bandeau l'indique) :

- **document protégé** par un mot de passe, ou dont l'auteur a interdit la modification : Quai PDF ne retire jamais une protection ;
- **structure que Quai PDF ne sait pas réécrire sans risque** : le document reste lisible, et « Enregistrer sous » en fait une copie identique.

## Vos données

- Réglages : `%LOCALAPPDATA%\QuaiPdf\settings.json` (thème, vignettes).
- Journaux : `%LOCALAPPDATA%\QuaiPdf\logs` (7 jours). Ils contiennent les noms des fichiers ouverts, jamais leur contenu ni de mot de passe.
- Quai PDF ne se connecte à rien.

## Limites actuelles

Quai PDF est un lecteur / éditeur **de base**. Pas encore de recherche dans le texte, de sélection ou de copie de texte, d'annotations, de formulaires, de signature, d'impression ni de modification du texte des pages.
