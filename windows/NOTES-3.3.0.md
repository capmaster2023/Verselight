# MyChurch 3.3.0 : écran par défaut, écrans principaux du déroulé et nouveau design ivoire, bleu nuit et or

**Pas encore essayé sur du vrai matériel d'église (projecteur, caméras, OBS). Faites un essai complet avant de l'utiliser pendant un culte.**

## Écran par défaut : Retirer de l'écran revient direct dessus

Avant, **Retirer de l'écran** (Échap) laissait l'écran vide. Maintenant, l'écran revient direct sur l'**écran par défaut**, choisi dans la nouvelle carte **Écran par défaut** de la colonne de droite (sur toutes les pages) :

- **Noir** : l'écran se vide, comme avant (choix de départ : rien ne change tant que vous n'en choisissez pas un autre) ;
- **Fond seul** : l'habillage de l'écran (Aube, Nuit…), sans texte ;
- **un écran principal du déroulé** (voir plus bas) : un logo, une image, une caméra, une fenêtre, une vidéo ou un texte de bienvenue.

Chaque choix montre sa miniature ; celui qui est choisi est en or. Le bouton **Retirer de l'écran** rappelle où l'écran va revenir (« Retour direct : Logo »).

Sur l'écran par défaut, **Suivant** reprend juste après ce qui a été retiré (la strophe ou le verset suivant) et **Précédent** remet ce qui a été retiré.

L'écran par défaut est pour la salle : **OBS, la caméra virtuelle et la page publique restent vides**, comme avant quand on retirait les paroles. Le contenu épinglé du Duo reste.

## Écrans principaux dans le déroulé

**+ Ajouter au déroulé › Écran principal** : le fond seul, ou une source de la bibliothèque Display (image, caméra, fenêtre, vidéo, texte). Dans Display, le clic droit sur une source propose aussi **Ajouter au déroulé comme écran principal**.

Quand un écran principal passe à l'écran (clic, double-clic ou Suivant), **il reste à l'écran et devient l'écran par défaut** : par exemple le logo avant le culte, la caméra de la chaire pour la prédication, le fond seul à la fin. Sa carte porte **PAR DÉFAUT** quand c'est lui qui est choisi ; le clic droit propose **Choisir comme écran par défaut**.

## Nouveau design

- **Couleurs** : ivoire et sable, texte bleu nuit, accent or ; les boutons principaux (Suivant, Importer, Ajouter) sont bleu nuit. Le thème sombre ne change pas.
- **Barre de gauche** : quand la fenêtre est assez large (1360 px et plus), le logo (une croix au-dessus des vagues), « MyChurch » et le nom de chaque page ; plus étroite, la barre compacte d'icônes revient.
- **Déroulé** : chaque carte a l'icône de son type sur sa couleur pâle, « heure · TYPE », le titre et le détail ; **À L'ÉCRAN** en rouge sur l'élément à l'écran, **PAR DÉFAUT** en or sur l'écran principal choisi.
- **Page Culte** : l'**écran live** en grand au centre (ce qui est à l'écran, ce qui vient ensuite, Précédent, Suivant, Noir, Retirer de l'écran et « Retour direct »), puis toutes les diapositives du culte en vignettes. La colonne de droite garde l'écran par défaut, l'audio, le Duo et l'habillage.
- **Display** : chaque tuile de la grille montre son type (texte, image, caméra, fenêtre, Duo) au-dessus de la vignette.

## Licences

- MyChurch : GNU GPL version 2 ou ultérieure (à cause de libobs) — https://github.com/capmaster2023/Verselight
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 (`ffmpeg.exe` seul, programme séparé) : GNU GPL version 3, texte dans `ffmpeg\LICENSE.txt`.
- NAudio (lecture des entrées de la carte son) : licence MIT.
- Polices Manrope, IBM Plex Mono, Literata, Atkinson Hyperlegible Next et Unbounded : SIL Open Font License 1.1 (textes dans `windows/MyVerse/Polices`).
