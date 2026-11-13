# MyChurch 4.2.0

Logiciel Windows autonome de projection pour l'église, avec télécommande Android. Bible, chants et sources Display partent directement sur l'écran choisi dans **Projection**. Le son passe par le moteur audio d'OBS Studio (libobs), avec un mélangeur simplifié comme celui d'OBS.

## Premier lancement

MyChurch démarre **vide** : aucun chant, aucune source audio, aucun enregistrement. Seules deux Bibles libres de droits sont livrées : **Louis Segond 1910** (français) et **King James** (anglais). Les chants s'ajoutent avec **+ Importer chants ou recueil** ou **+ Nouveau chant**, les autres Bibles avec **+ Importer une Bible**.

MyChurch est un logiciel libre sous licence **GNU GPL version 2 ou ultérieure** (voir [Licence et code source](#licence-et-code-source)).

## Interface (3.2, design « Pupitre »)

- **Barre de gauche** : **Culte**, **Bible**, **Chants**, **Display**, **Duo**, puis en bas **Sombre / Clair** et **Réglages**.
- **Page Culte** : le déroulé en cartes à gauche (un trait de couleur par type, « ● DIRECT » sur l'élément à l'écran) et, au centre, **toutes les diapositives du culte en vignettes**, dessinées comme à l'écran et groupées par élément. La vignette à l'écran est encadrée de rouge (« À L'ÉCRAN »), la suivante d'ambre (« SUIVANT »). Un clic affiche une vignette.
- **Colonne de contrôle à droite, sur toutes les pages** : un petit écran **EN DIRECT**, la diapositive **SUIVANT** en plus petit, **◀ / Suivant ▶**, **Noir / Retirer de l'écran**, puis l'**audio en bref** (une ligne par source avec son vumètre et sa sourdine **M**, et **+ Source**). Duo et habillage sont dessous.
- **Barre du bas** : les enregistrements (un clic : ● démarrer, ■ arrêter, avec le chrono), les sorties MyVirtualSound (« A · Zoom »), l'écran de projection, les téléphones, l'écran de scène et l'heure.
- Thème **clair** par défaut (crème), **sombre** au choix d'un clic. Police Manrope.

## Duo : deux contenus à l'écran

L'onglet **Duo** prépare des dossiers de 2 éléments, chacun à sa place et à sa taille : **Choisir › Chant…** (recueil, puis numéro ou titre), **Verset…** ou une source Display. Dans la grille de Display, la vignette d'un dossier montre ses deux éléments comme sur l'écran principal.

Pour épingler à la volée :

1. Affichez le premier contenu.
2. Dans la carte **DUO**, cliquez **Épingler le contenu en direct**, ou appuyez sur **P**.
3. Affichez le second contenu : le premier reste dans un coin.
4. Choisissez un coin ou **Côte à côte**. La taille d'un coin se règle entre 20 et 50 %.

**Retirer de l'écran** ne vide que le contenu principal. **Retirer le duo** enlève le contenu épinglé. La touche **B** masque les deux sans les perdre.

Textes, versets, chants, images, caméras et fenêtres peuvent être épinglés. Une source vidéo épinglée continue de fonctionner pendant l'affichage de l'autre contenu. Le Duo se retrouve dans la projection, l'aperçu et **MyVirtualCam**.

## Display

- **+ Ajouter une source** ouvre un menu : Nouveau texte, Images, Périphérique de capture vidéo, Capture de fenêtre et MyVirtualCam.
- **+** à côté d'une source l'ajoute à la grille ; le bouton devient **✓**, et un clic sur ✓ l'en retire. **Modifier** est dans le clic droit.
- Un clic sur une vignette l'envoie immédiatement sur l'écran choisi.
- Clic droit : Afficher puis épingler, Modifier, Retirer de la grille, Ajouter au déroulé du culte ou Supprimer.

## Audio (moteur OBS, simplifié comme OBS)

Le panneau **Audio** se trouve dans l'onglet **Display**, sous la grille. Tout le son est traité par **libobs**, la bibliothèque d'OBS Studio 33.0.1, livrée dans le dossier `obs` à côté de `MyChurch.exe`, avec les réglages par défaut d'OBS (48 kHz, stéréo).

- **+ Source audio**, comme le « + » des sources d'OBS : **Capture audio (entrée)**, **Capture audio (sortie)**, **Média**, ou le son de la vidéo projetée depuis Display.
- **Propriétés** d'une capture audio, comme dans OBS : **Périphérique** (« Par défaut » ou un périphérique) et **Utiliser l'horodatage du périphérique**. Une carte son à plusieurs entrées (Behringer, Focusrite…) propose aussi **Entrée** : toutes, ou une seule (un micro par source).
- **Mélangeur** : une bande par source avec son volume, deux vumètres, un fader de −60 à +6 dB, **M** (sourdine), le **casque** (monitoring) et **⋮** (Propriétés, Filtres, Renommer, Propriétés audio avancées, déplacer, Supprimer).
- **⚙ Propriétés audio avancées**, comme dans OBS : état, volume (dB ou %), mono, balance, décalage de synchronisation, monitoring audio et **pistes 1 à 6**. Une nouvelle source va dans les 6 pistes.
- **Filtres d'OBS** : réduction du bruit (RNNoise ou Speex), noise gate, compresseur, limiteur, en direct.
- **Enregistrements** (5 au plus, qui peuvent tourner en même temps) : comme dans OBS, chacun garde **une piste** (1 à 6). Avec des sources **Display** (projection, caméras, fenêtres, images, textes, dossiers Duo), c'est une vidéo MKV (incrustation ou côte à côte, synchro du son) ; sinon WAV ou FLAC. FFmpeg 8.0.1 est inclus (dossier `ffmpeg`).
- **MyVirtualSound** (4 sorties au plus, actives en même temps) : chacune envoie **une piste** vers un câble audio virtuel ; Zoom ou un navigateur la reçoivent comme un micro (« CABLE Output »). VB-CABLE (VB-Audio Software, www.vb-cable.com, donationware) n'est pas inclus : **Réglages › MyVirtualSound › Installer VB-CABLE** télécharge le paquet officiel, seulement sur clic.
- **Réglages › Audio** : seulement le périphérique de monitoring audio (casque de la régie).
- Une source dont le périphérique est débranché devient « Inactif » et revient quand il est rebranché ; rien n'est ajouté tout seul.
- Si le dossier `obs` manque, le panneau propose **Réparer le moteur audio** (réinstallation, données gardées).

## Projection et caméra virtuelle

Le bouton **Projection** (en bas à droite) propose les moniteurs détectés, leurs dimensions et une sortie en fenêtre. MyChurch projette directement : aucun logiciel externe ni connexion WebSocket n'est nécessaire.

**MyVirtualCam** reçoit automatiquement la sortie en 720p30, Duo compris. Elle s'enregistre une fois avec l'autorisation Windows et reste disponible, avec une image noire, quand MyChurch est fermé. Elle vise les applications compatibles DirectShow et ne transporte pas d'audio.

La capture de fenêtre nécessite une fenêtre ouverte et non réduite. Certains contenus protégés ou accélérés par le GPU ne permettent pas la capture. Les périphériques vidéo physiques nécessitent les pilotes et permissions Windows appropriés.

## Installation et mises à jour

[Téléchargements de la 4.2.0](https://github.com/capmaster2023/Verselight/releases/tag/v4.2.0) :

- `MyChurch-Setup-4.2.0.exe` : installateur Windows.
- `MyChurch-4.2.0-Windows.zip` : version portable.
- `MyChurch.apk` : télécommande Android.

Aucun jeton GitHub n'est intégré dans l'application.

L'installation conserve les chants, Bibles, réglages et téléphones jumelés. Une DLL MyVirtualCam déjà utilisée est renommée avant la copie de sa nouvelle version; le logiciel qui l'utilise charge la nouvelle caméra à sa prochaine ouverture.

Sur le téléphone, installez l'APK par-dessus l'application existante. Le package Android et la signature existante restent conservés. Le PC et le téléphone doivent être sur le même réseau pour la télécommande.

## Licence et code source

MyChurch est un logiciel libre : vous pouvez le redistribuer et le modifier selon les termes de la **GNU General Public License, version 2 ou (à votre choix) toute version ultérieure**, publiée par la Free Software Foundation. Cette licence s'applique parce que MyChurch utilise libobs (OBS Studio), elle-même sous GPL v2 ou ultérieure. Le texte complet est dans [`COPYING`](COPYING), installé avec MyChurch sous le nom `COPYING.txt` (**Réglages › Licences › Lire la licence GNU GPL**).

- Code source de MyChurch : https://github.com/capmaster2023/Verselight
- Code source d'OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 (`ffmpeg.exe`, build « essentials » de gyan.dev) : programme séparé sous **GNU GPL version 3**, licence dans `ffmpeg\LICENSE.txt`, sources sur https://ffmpeg.org/releases/

Le dossier `obs` livré avec MyChurch contient les bibliothèques du paquet officiel OBS Studio 33.0.1 pour Windows (sans l'interface d'OBS), avec le runtime Microsoft Visual C++ dont elles ont besoin. Les polices de l'interface (Inter, Space Grotesk, Literata, Atkinson Hyperlegible Next, Unbounded) sont sous licence SIL Open Font License 1.1.

## Développement et validation

- `windows/MyVerse.Core` : cœur .NET 8, sans WPF.
- `windows/MyVerse` : interface Windows WPF.
- `windows/MyVerse.Tests` : tests du cœur et du protocole téléphone, dont Duo, et du mélangeur sur la vraie libobs (groupe « Audio OBS »).
- `windows/MyVirtualCam` : composant vidéo DirectShow 32 et 64 bits.
- `android` : télécommande Kotlin / Compose.
- `windows/NOTES-<version>.md` : notes de version (la dernière : `NOTES-4.2.0.md`).

Tests locaux : `dotnet run --project windows/MyVerse.Tests -c Release -- windows/MyVerse/Ressources [groupe]`. Les essais de libobs utilisent le dossier OBS donné par la variable `MYCHURCH_OBS` sous Windows, ou le paquet `obs-studio` sous Linux (avec `xvfb`). Les essais FLAC et MKV (dont la synchro son/image mesurée) utilisent le `ffmpeg` donné par `MYCHURCH_FFMPEG`.

GitHub Actions **Build MyChurch** télécharge le paquet officiel d'OBS 33.0.1 et FFmpeg 8.0.1 (SHA-256 vérifiés), ne garde d'OBS que libobs et trois modules (win-wasapi, obs-filters, obs-ffmpeg), vérifie leurs dépendances, enregistre de vrais fichiers WAV, FLAC et MKV, compile et teste WPF sur Windows, vérifie les images de projection, les pixels de MyVirtualCam, l'export navigateur local et l'installation, puis construit les APK. La branche `dev` sert à valider. Une avance de `main` publie la version. Les comptes rendus et captures sont copiés dans `ci-logs`.
