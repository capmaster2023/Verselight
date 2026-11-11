# MyChurch 3.0.1 : mélangeur vertical, enregistrements et sorties à la carte

**Pas encore essayé sur du vrai matériel d'église (micros, tables de mixage, cartes son, caméras, Zoom). Faites un essai complet — un enregistrement vidéo de quelques minutes, une sortie vers Zoom — avant de l'utiliser pendant un culte.**

## Mélangeur vertical, comme OBS

Chaque source est une bande verticale : son nom, son volume en dB, l'échelle de **0 à −60 dB** (un chiffre tous les 5 dB), deux vumètres (gauche et droite, avec un repère de crête) et un fader de −60 à +6 dB. Dessous : **M** (sourdine), le **casque** (écoute coupée, seule, ou écoute et sortie) et **Filtres**. Double-clic sur le fader : 0 dB ; la molette règle par pas de 1 dB.

Les pastilles en bas de chaque bande disent où va la source : **1 à 5** pour les enregistrements, **A à D** pour les sorties MyVirtualSound. Un clic l'ajoute ou la retire. Une pastille rouge veut dire « enregistrement en cours », bleue « sortie active ».

## Enregistrements : rien de préréglé, jusqu'à 5 en même temps

**+ Créer un enregistrement** demande :

- son **nom** et ses **sources audio** (les bandes du mélangeur) ;
- **Couper les autres sources pendant cet enregistrement** : tant qu'il tourne, les sources non cochées sont coupées partout (casque, Zoom, autres enregistrements). Exemple : « Prédication » (pasteur seul) coupe la guitare, le piano et le chanteur ; « Louange » peut couper le micro du pasteur ;
- ses **sources Display** (facultatif) : la projection et toutes les sources de la bibliothèque (caméras, fenêtres, images, vidéos, textes, dossiers Duo). Avec au moins une, le fichier est une **vidéo MKV** ; on les coche dans l'ordre, la 1re est en grand. Disposition : **incrustation** (les autres en petit en bas à droite) ou **côte à côte** (chaque image garde la forme de l'écran) ;
- la **synchro du son** (de −500 à +1000 ms) pour une caméra en retard ;
- le **dossier** (par défaut `Vidéos\MyChurch\<nom>`) et, pour le son seul, **WAV** ou **FLAC**.

Dès qu'un enregistrement existe, le bouton propose le suivant (5 au plus). Les 5 peuvent tourner **en même temps** : chacun a son bouton ● / ■, sa durée, la taille du fichier et son vumètre. Clic droit ou **⋯** : Modifier, Ouvrir le dossier, Montrer le dernier fichier, Supprimer.

**Son et image ensemble** : la vidéo est cadencée par l'horloge du son (aucune dérive, même après des heures). Un essai automatique mesure un « clap » (son fort et image claire au même moment) dans de vrais fichiers MKV : l'écart doit rester sous 50 ms, et un décalage réglé de ±200 ms doit se retrouver tel quel dans le fichier. Une caméra USB a souvent un retard propre de 50 à 200 ms : réglez **Synchro du son** une fois, en regardant les lèvres.

**FFmpeg est maintenant inclus** (dossier `ffmpeg`) : FLAC et vidéo marchent sans rien installer. La vidéo utilise l'encodeur H.264 de la carte graphique (NVIDIA, Intel ou AMD) s'il marche, sinon x264. Le MKV reste lisible même après une coupure de courant.

## MyVirtualSound : plusieurs sorties à la fois

Même logique que les enregistrements : **+ Créer une sortie** choisit son nom, son câble virtuel ou sa sortie, ses sources et l'option « couper les autres sources ». Jusqu'à 4 sorties, actives en même temps si besoin (par exemple Zoom avec le pasteur seul, et un direct avec toute la louange). Chaque carte a son interrupteur, son volume, sa sourdine et son vumètre.

VB-CABLE n'est toujours pas inclus : il s'installe seulement sur clic, dans **Paramètres › MyVirtualSound** (paquet officiel). VB-CABLE est un logiciel de VB-Audio Software (www.vb-cable.com), distribué en donationware.

## Pistes d'OBS automatiques

On ne choisit plus de « piste » : MyChurch donne une piste d'OBS à chaque mélange différent en cours (enregistrements qui tournent, sorties actives). Deux destinations avec les mêmes sources partagent une piste. OBS en a 6 : au-delà de 6 mélanges différents en même temps, le panneau le dit et propose d'en arrêter un.

## Duo

- **Choisir › Chant…** ouvre une fenêtre avec le **recueil** (tous vos recueils et leur nombre de chants), puis le numéro ou des mots du titre. **Choisir › Verset…** prend un passage (« jean 3 16 »).
- Dans la grille de Display, la vignette d'un dossier Duo montre maintenant ses deux éléments **comme sur l'écran principal**, à leur place et à leur taille (au lieu de « Duo · 2 éléments »), et suit les changements faits dans l'onglet Duo.

## Moteur OBS absent

Si le dossier `obs` manque ou ne se charge pas (fichier supprimé, antivirus), le panneau Audio l'explique et propose **Réparer le moteur audio** : MyChurch télécharge sa dernière version sur GitHub et la réinstalle (chants, agendas et réglages gardés).

## Reprise des réglages de la 3.0.0

- La sortie MyVirtualSound de la 3.0.0 devient la sortie **« Zoom »**, avec les sources qui allaient dans son ancienne piste.
- Le profil d'enregistrement automatique de la 3.0.0 n'est pas repris : créez vos enregistrements (aucun préréglage).
- Les sources du mélangeur, leurs volumes, filtres et écoutes sont gardés.

## Licences

- MyChurch : GNU GPL version 2 ou ultérieure (à cause de libobs) — https://github.com/capmaster2023/Verselight
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- **FFmpeg 8.0.1** (build « essentials » de gyan.dev, `ffmpeg.exe` seul) : programme séparé, **GNU GPL version 3**, texte dans `ffmpeg\LICENSE.txt`. Sources : https://ffmpeg.org/releases/ · build : https://www.gyan.dev/ffmpeg/builds/
