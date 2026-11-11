# MyChurch 3.2.0 : design « Pupitre », un petit écran en direct et toutes les diapositives du culte

**Pas encore essayé sur du vrai matériel d'église (cartes son à plusieurs entrées, micros, caméras, Zoom). Faites un essai complet avant de l'utiliser pendant un culte.**

## L'écran en direct devient petit

L'écran en direct ne prend plus le milieu de la page. Il est en petit, en haut de la **colonne de contrôle à droite**, présente sur toutes les pages (sauf Réglages) :

- **EN DIRECT** : ce qui part sur l'écran de projection (double-clic : découper la diapositive) ;
- **SUIVANT** : la diapositive qui vient avec Suivant, en plus petit, avec **Découper** à côté ;
- **◀** et **Suivant ▶**, puis **Noir** (touche B, le bouton s'inverse) et **Retirer de l'écran** (Échap) ;
- **Audio** : une ligne par source du mélangeur, avec l'entrée de la carte son (« Entrée 1 · UMC404HD »), son vumètre et sa sourdine **M** ; **+ Micros** ajoute une bande par entrée. Le mélangeur complet reste dans Display ;
- dessous : Duo et l'habillage de l'écran.

## Page Culte : toutes les diapositives en vignettes

- **À gauche, le déroulé en cartes** : un trait de couleur par type (chant bleu, lecture ambre, accueil gris, Display rose, Duo turquoise), « heure · type · détail », **● DIRECT** sur l'élément à l'écran ; les éléments passés pâlissent. Le menu ⌄ change de culte, en crée un, le renomme, **envoie le déroulé ou le résumé sur WhatsApp**, ou le supprime.
- **Au centre, toutes les diapositives du culte**, groupées par élément (« 10:30 · Jean 3:16-17 · 2 diapositives »). Chaque vignette est dessinée comme à l'écran, avec l'habillage choisi. La vignette à l'écran est encadrée de rouge (**À L'ÉCRAN**), la suivante d'ambre (**SUIVANT**). **Un clic affiche une vignette**, un clic droit ouvre le menu de l'élément. Les vignettes suivent le direct pendant le culte.
- Un clic sur une carte du déroulé fait défiler les vignettes jusqu'à elle ; double-clic : afficher depuis le début.

## Barre du bas

Les **enregistrements** en pastilles (un clic : ● démarrer, ■ arrêter, avec le chrono), les sorties **MyVirtualSound** (« A · Zoom active », un clic l'active ou la coupe), puis à droite l'écran de **Projection** (un clic : choisir l'écran), les **téléphones**, l'**écran de scène** et l'**heure**. **Mettre à jour** y apparaît quand une nouvelle version existe.

## Nouveau style

- Thème clair crème (par défaut) et thème sombre brun très foncé ; le bouton **Sombre / Clair** de la barre de gauche change de thème d'un clic (aussi dans Réglages › Apparence).
- Police **Manrope** pour l'interface et **IBM Plex Mono** pour les heures et les chronos (incluses). La projection garde ses polices.

## Licences

- MyChurch : GNU GPL version 2 ou ultérieure (à cause de libobs) — https://github.com/capmaster2023/Verselight
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 (`ffmpeg.exe` seul, programme séparé) : GNU GPL version 3, texte dans `ffmpeg\LICENSE.txt`.
- NAudio (lecture des entrées de la carte son) : licence MIT.
- Polices Manrope, IBM Plex Mono, Literata, Atkinson Hyperlegible Next et Unbounded : SIL Open Font License 1.1 (textes dans `windows/MyVerse/Polices`).
