# MyChurch 3.1.0 : nouvelle interface, thème clair ou sombre, un micro par bande

**Pas encore essayé sur du vrai matériel d'église (cartes son à plusieurs entrées, micros, caméras, Zoom). Faites un essai complet avant de l'utiliser pendant un culte : ajoutez vos micros, parlez dans chacun et regardez si le bon vumètre bouge.**

## Nouvelle interface

- **Une barre de pages à gauche** : Culte, Bible, Chants, Display, Duo, et Réglages en bas.
- **Un en-tête par page** : son titre (sur Culte, le nom du culte, avec un menu pour en changer), l'étape en cours (« 10:30 · étape 3 / 6 »), la recherche et les boutons Téléphone, Écran de scène et Projection.
- **Culte** : le déroulé à gauche, l'écran en direct au centre (Précédent, Suivant, Retirer de l'écran, ce qui vient ensuite, les enregistrements et MyVirtualSound), l'élément choisi à droite. Ses diapositives sont des cartes : « ● À L'ÉCRAN » en rouge, « SUIVANT » en ambre. Deux versets tiennent côte à côte.
- **Bible, Chants, Display et Duo** : la même disposition, avec l'écran en direct dans la colonne de droite.

## Thème clair par défaut, thème sombre au choix

**Réglages › Apparence** : **Clair** (par défaut) ou **Sombre**, pour une salle obscure. Le changement est immédiat, dans toutes les fenêtres, et il est gardé. La projection garde son propre habillage (Aube, Nuit, Noir…).

## Display : le + de la bibliothèque

Chaque source de la bibliothèque a son **+** : un clic la met dans la grille, et le bouton devient **✓**. Un clic sur ✓ la retire de la grille (elle reste dans la bibliothèque). **Modifier** passe dans le clic droit.

## Un micro par bande, comme dans OBS

Une carte son à plusieurs entrées (Behringer UMC404HD, Focusrite…) arrive dans Windows comme un seul micro : jusqu'ici, tous ses micros tombaient dans une seule bande du mélangeur.

**+ Source audio › Micros (une bande par entrée)…** montre chaque micro de Windows avec ses entrées. Cochez chaque entrée où un micro est branché : chacune devient **sa propre bande**, avec son volume, ses filtres, son écoute au casque, ses vumètres et ses pastilles (enregistrements 1 à 5, sorties A à D). Sous le nom de la bande : « Micro · entrée 2 » et le nom de la carte son.

- Clic droit sur une bande › **Entrée** : choisir une autre entrée, ou **Toutes les entrées ensemble** (une bande stéréo, comme avant).
- MyChurch écoute la carte son une seule fois (partagée avec Windows et Zoom) et donne à chaque bande son canal.
- Les micros de la 3.0 gardent « toutes les entrées ensemble » : rien ne change tant que vous n'ajoutez pas de micro par entrée.

## Réglages en page

Les Paramètres ne sont plus une fenêtre : la page **Réglages** a un sommaire à gauche (Apparence, Audio, MyVirtualSound, Enregistrements, Projection, Téléphone, Mise à jour, Transfert, Journal, Licences), qui suit le défilement. **Projection** liste les écrans avec « Projeter ici » ou « Arrêter ». Le numéro de version, en haut de la barre de gauche, ouvre **Mise à jour**.

## Licences

- MyChurch : GNU GPL version 2 ou ultérieure (à cause de libobs) — https://github.com/capmaster2023/Verselight
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 (`ffmpeg.exe` seul, programme séparé) : GNU GPL version 3, texte dans `ffmpeg\LICENSE.txt`.
- NAudio (lecture des entrées de la carte son) : licence MIT.
