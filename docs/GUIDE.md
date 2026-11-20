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

## Sélectionner et copier du texte

| Action | Comment |
|---|---|
| Sélectionner | appuyez sur le texte et faites glisser (les pages défilent si vous sortez de la zone de lecture) |
| Un mot | double-clic |
| Étendre la sélection | `Maj+clic` |
| Tout le document | `Ctrl+A`, ou clic droit, **Tout sélectionner** |
| Copier | `Ctrl+C`, ou clic droit, **Copier** |
| Effacer la sélection | un clic ailleurs, ou `Échap` |

Une page scannée (une simple image) n'a pas de texte à sélectionner. Si l'auteur du document a interdit la copie de son texte, Quai PDF le respecte et le dit.

## Modifier

La vignette de la page affichée est sélectionnée (barre de couleur à sa gauche) et suit le défilement. Les modifications s'appliquent aux **pages sélectionnées dans les vignettes** : la page affichée, ou plusieurs pages choisies avec `Ctrl+clic` / `Maj+clic` (une sélection de plusieurs pages reste en place quand les pages défilent). `Échap` dans les vignettes vide la sélection.

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

## Mises à jour

Quand une nouvelle version est publiée, un bandeau l'annonce en haut de la fenêtre. **Mettre à jour** :

1. télécharge l'installateur (bouton **Annuler** pendant le téléchargement) et vérifie son empreinte SHA-256 : un fichier qui ne correspond pas à celui publié est supprimé sans être exécuté ;
2. si le document a des modifications non enregistrées, propose de les enregistrer (**Annuler** : la mise à jour attend, déjà téléchargée) ;
3. ferme Quai PDF, installe la nouvelle version sans rien demander, puis relance Quai PDF, qui rouvre votre document.

Avec la version portable (zip), le bouton ouvre la page de la version sur GitHub.

## Lecture seule

Certains documents s'affichent sans pouvoir être modifiés (un bandeau l'indique) :

- **document protégé** par un mot de passe, ou dont l'auteur a interdit la modification : Quai PDF ne retire jamais une protection ;
- **structure que Quai PDF ne sait pas réécrire sans risque** : le document reste lisible, et « Enregistrer sous » en fait une copie identique.

## Vos données

- Réglages : `%LOCALAPPDATA%\QuaiPdf\settings.json` (thème, vignettes).
- Journaux : `%LOCALAPPDATA%\QuaiPdf\logs` (7 jours). Ils contiennent les noms des fichiers ouverts, jamais leur contenu ni de mot de passe.
- Au démarrage, Quai PDF demande à GitHub le numéro de la dernière version publiée (rien sur vous ni sur vos documents). Désactivable dans les **Réglages**. C'est sa seule connexion.
- Installateur d'une mise à jour : `%LOCALAPPDATA%\QuaiPdf\updates`, effacé au démarrage suivant.

## Limites actuelles

Quai PDF est un lecteur / éditeur **de base**. Pas encore de recherche dans le texte, de reconnaissance du texte des pages scannées, d'annotations, de formulaires, de signature, d'impression ni de modification du texte des pages.
