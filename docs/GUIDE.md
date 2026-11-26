# Guide de Quai PDF

## Ouvrir des documents

Chaque document s'ouvre dans son **onglet**, en haut de la fenêtre (comme dans le Bloc-notes ou l'Explorateur).

- Bouton **Ouvrir** (`Ctrl+O`), le **+** à droite des onglets (`Ctrl+T`), ou glissez un ou plusieurs PDF dans la fenêtre depuis l'Explorateur.
- Clic droit sur un PDF dans l'Explorateur, **Ouvrir avec**, **Quai PDF** : si Quai PDF est déjà ouvert, le PDF arrive dans un nouvel onglet de la fenêtre existante (une seule fenêtre de Quai PDF).
- Un fichier déjà ouvert n'est pas ouvert deux fois : son onglet est affiché.
- Un document protégé par un mot de passe le demande à l'ouverture. Le mot de passe n'est jamais enregistré.

| Onglets | Comment |
|---|---|
| Onglet suivant / précédent | `Ctrl+Tab` / `Ctrl+Maj+Tab`, ou `Ctrl+Pg.Suiv` / `Ctrl+Pg.Préc` |
| Fermer l'onglet | la croix de l'onglet, `Ctrl+W` ou `Ctrl+F4` |
| Changer l'ordre | faites glisser l'onglet |

Fermer le dernier onglet ramène à l'accueil. Un point à côté du nom d'un onglet signale des modifications non enregistrées.

## Lire

| Action | Comment |
|---|---|
| Page suivante / précédente | flèches de la barre d'état (en bas), molette, `Pg.Suiv` / `Pg.Préc` |
| Aller à une page | tapez son numéro dans la barre d'état |
| Zoom avant / arrière | `Ctrl+Plus` / `Ctrl+Moins`, `Ctrl+molette`, curseur ou menu du zoom de la barre d'état |
| Taille réelle | `Ctrl+0` |
| Ajuster à la largeur | bouton à gauche du curseur du zoom, ou menu du zoom |

La barre d'état rappelle aussi l'état du document : **Enregistré**, **Modifié, pas encore enregistré** ou **Lecture seule**.

## Volets

Le rail d'icônes, à gauche, ouvre un volet ; un second clic sur la même icône le referme.

| Volet | Contenu |
|---|---|
| **Pages** | vignettes des pages : sélection, glisser-déposer pour changer l'ordre |
| **Plan** | signets du document (chapitres) ; un clic montre la page |
| **Recherche** | occurrences du texte cherché, avec un extrait ; un clic montre la page |
| **Propriétés** | titre, auteur, sujet, mots-clés, nombre de pages, taille, emplacement, droits de modification et de copie ; **Modifier les propriétés…** (`Ctrl+D`) |

## Rechercher

Tapez un mot ou une expression dans le champ **Rechercher**, en haut à droite (`Ctrl+F`), puis `Entrée`. Les majuscules et les accents ne comptent pas (« ecole » trouve « École »), et une expression coupée en fin de ligne est trouvée. L'occurrence montrée est sélectionnée : `Ctrl+C` la copie.

| Action | Comment |
|---|---|
| Résultat suivant | `F3`, ou `Entrée` de nouveau dans le champ |
| Résultat précédent | `Maj+F3` |

Une page scannée (une simple image) n'a pas de texte : la recherche ne la voit pas. Après une modification (pages pivotées, supprimées, déplacées…), la recherche est relancée d'elle-même.

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
| Enregistrer / enregistrer sous | `Ctrl+S` / `Ctrl+Maj+S`, ou le bouton **Enregistrer** et sa flèche (sur un document en lecture seule, **Enregistrer** en fait une copie) |
| Fermer l'onglet | `Ctrl+W` |

Les commandes de pages les plus courantes (**Pivoter**, **Supprimer**) sont dans la barre ; les autres (**Monter**, **Descendre**, **Insérer**, **Extraire**, **Propriétés**…) dans le menu **Plus**.

Rien n'est écrit sur le disque avant **Enregistrer**. Un point à côté du nom de l'onglet signale des modifications non enregistrées ; Quai PDF les propose à l'enregistrement, document par document, avant de fermer un onglet ou la fenêtre.

## Zones de texte

Pour écrire sur un PDF (remarque, réponse, correction), comme dans Firefox, Chrome, Edge ou Acrobat :

| Action | Comment |
|---|---|
| Ajouter une zone de texte | bouton **Texte**, puis un clic sur la page ; tapez (`Entrée` va à la ligne), puis cliquez ailleurs ou `Ctrl+Entrée` |
| Modifier le texte | double-clic sur la zone (ou un clic avec l'outil **Texte**, ou `Entrée` sur la zone sélectionnée) |
| Déplacer | faites glisser la zone |
| Supprimer | sélectionnez-la d'un clic, puis `Suppr` (ou videz son texte) |
| Taille et couleur | bouton à droite de **Texte** : pour les prochaines zones et pour la zone sélectionnée |
| Abandonner la saisie | `Échap` |
| Quitter l'outil **Texte** | `Échap`, ou un clic sur **Texte** |

Les zones de texte ajoutées dans Firefox, Chrome, Edge ou Acrobat se modifient, se déplacent et se suppriment de la même façon. Chaque changement s'annule avec `Ctrl+Z`, et rien n'est écrit sur le disque avant **Enregistrer**.

Ce que Quai PDF respecte : une zone que son auteur a verrouillée ne bouge pas (ou son texte ne change pas, selon le verrou) ; une zone avec une flèche de rappel (Acrobat) s'affiche telle quelle mais ne se modifie pas ici. Le texte est toujours gardé tel que tapé, et s'affiche partout en Helvetica : les caractères que cette police ne connaît pas (alphabets non latins, symboles rares) y apparaissent comme « ? », bien que le texte exact soit enregistré.

## Mises à jour

Quand une nouvelle version est publiée, un bandeau l'annonce en haut de la fenêtre. **Mettre à jour** :

1. télécharge l'installateur (bouton **Annuler** pendant le téléchargement) et vérifie son empreinte SHA-256 : un fichier qui ne correspond pas à celui publié est supprimé sans être exécuté ;
2. si des documents ont des modifications non enregistrées, propose de les enregistrer (**Annuler** : la mise à jour attend, déjà téléchargée) ;
3. ferme Quai PDF, installe la nouvelle version sans rien demander, puis relance Quai PDF, qui rouvre vos documents, chacun dans son onglet.

Avec la version portable (zip), le bouton ouvre la page de la version sur GitHub.

## Lecture seule

Certains documents s'affichent sans pouvoir être modifiés (un bandeau l'indique) :

- **document protégé** par un mot de passe, ou dont l'auteur a interdit la modification : Quai PDF ne retire jamais une protection ;
- **structure que Quai PDF ne sait pas réécrire sans risque** : le document reste lisible, et « Enregistrer sous » en fait une copie identique.

## Vos données

- Réglages : `%LOCALAPPDATA%\QuaiPdf\settings.json` (thème, volet latéral).
- Journaux : `%LOCALAPPDATA%\QuaiPdf\logs` (7 jours). Ils contiennent les noms des fichiers ouverts, jamais leur contenu ni de mot de passe.
- Au démarrage, Quai PDF demande à GitHub le numéro de la dernière version publiée (rien sur vous ni sur vos documents). Désactivable dans les **Réglages**. C'est sa seule connexion.
- Installateur d'une mise à jour : `%LOCALAPPDATA%\QuaiPdf\updates`, effacé au démarrage suivant.

## Limites actuelles

Quai PDF est un lecteur / éditeur **de base**. Pas encore de reconnaissance du texte des pages scannées, d'autres annotations que les zones de texte (surlignage, dessin…), de formulaires, de signature, d'impression ni de modification du texte des pages.
