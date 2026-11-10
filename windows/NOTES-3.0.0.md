# MyChurch 3.0.0 : le moteur audio d'OBS et un nouveau design

**Pas encore essayé sur du vrai matériel d'église (micros, tables de mixage, cartes son, Zoom). Faites un essai complet avant de l'utiliser pendant un culte.**

## Moteur audio OBS (libobs 33.0.1)

Le son de MyChurch passe maintenant par **libobs**, la bibliothèque d'OBS Studio 33.0.1, livrée dans le dossier `obs` (sans l'interface d'OBS).

- **Mélangeur** dans l'onglet Display, sous la grille. Il démarre vide : **+ Source audio** ajoute un micro, le son d'une sortie, un fichier audio ou vidéo, ou le son de la vidéo projetée. Chaque bande a son volume (−60 à +6 dB), sa sourdine (**M**) et deux vumètres d'OBS.
- **Périphériques** : micros et entrées, son des sorties (haut-parleurs, casques), périphériques Windows par défaut. Le micro et la sortie proposés en premier se choisissent dans les Paramètres.
- **Filtres d'OBS** : compresseur, limiteur, noise gate, réduction du bruit (RNNoise ou Speex). On les active, les règle et les ordonne en direct.
- **Écoute au casque** avec les 3 modes d'OBS : écoute coupée, écoute seule (sortie coupée), écoute et sortie.
- **6 pistes indépendantes** : chaque source choisit ses pistes (boutons 1 à 6). Une piste s'enregistre (**● Enregistrer**, WAV ou FLAC) et une autre peut partir vers Zoom.

## MyVirtualSound avec VB-CABLE

MyVirtualSound envoie le mix d'une piste vers un câble audio virtuel : Zoom, Teams ou un navigateur le reçoivent comme un micro (« CABLE Output »). VB-CABLE n'est pas inclus. **Paramètres › MyVirtualSound › Installer VB-CABLE** télécharge le paquet officiel à la demande. VB-CABLE est un logiciel de VB-Audio Software (www.vb-cable.com), distribué en donationware.

## Nouveau design

Fond gris clair, cartes blanches, accent bleu, onglet actif en noir, police Inter. Barre du haut, bibliothèque Display, grille, colonne « En direct » et Paramètres redessinés (sections Périphériques audio, MyVirtualSound, Enregistrement, Code source et licences, Mise à jour).

## Retiré

- Le mixage audio local de la 1.2 bêta (bus, sources logiques, égaliseur…). Ses réglages ne sont pas repris : le mélangeur repart vide. Bible, chants, thème et autres réglages sont conservés.
- Le minuteur de prédication (et la commande « minuteur »).

## Licence et code source

Parce qu'il utilise libobs, MyChurch est distribué sous **GNU GPL version 2 ou ultérieure**. Le texte de la licence est installé (`COPYING.txt`) et visible dans **Paramètres › Code source et licences**.

- Code source de MyChurch : https://github.com/capmaster2023/Verselight
- Code source d'OBS Studio 33.0.1 : https://github.com/obsproject/obs-studio/tree/33.0.1
