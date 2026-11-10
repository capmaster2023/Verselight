# MyChurch 3.0.0

Logiciel Windows autonome de projection pour l'église, avec télécommande Android. Bible, chants et sources Display partent directement sur l'écran choisi dans **Projection**. Depuis la 3.0, le son passe par le moteur audio d'OBS Studio (libobs).

MyChurch est un logiciel libre sous licence **GNU GPL version 2 ou ultérieure** (voir [Licence et code source](#licence-et-code-source)).

## Duo : deux contenus à l'écran

1. Affichez le premier contenu.
2. Dans la carte **DUO**, cliquez **Épingler le contenu en direct**, ou appuyez sur **P**.
3. Affichez le second contenu : le premier reste dans un coin.
4. Choisissez un coin ou **Côte à côte**. La taille d'un coin se règle entre 20 et 50 %.

**Retirer de l'écran** ne vide que le contenu principal. **Retirer le duo** enlève le contenu épinglé. La touche **B** masque les deux sans les perdre.

Textes, versets, chants, images, caméras et fenêtres peuvent être épinglés. Une source vidéo épinglée continue de fonctionner pendant l'affichage de l'autre contenu. Le Duo se retrouve dans la projection, l'aperçu et **MyVirtualCam**.

## Display

- **+ Ajouter une source** ouvre un menu : Nouveau texte, Images, Périphérique de capture vidéo, Capture de fenêtre et MyVirtualCam.
- **+** à côté d'un titre l'ajoute à la grille. **Modifier** permet de le retoucher.
- Un clic sur une vignette l'envoie immédiatement sur l'écran choisi.
- Clic droit : Afficher puis épingler, Modifier, Retirer de la grille, Ajouter au déroulé du culte ou Supprimer.

## Audio (moteur OBS)

Le panneau **Audio** se trouve dans l'onglet **Display**, sous la grille. Tout le son est traité par **libobs**, la bibliothèque d'OBS Studio 33.0.1, livrée dans le dossier `obs` à côté de `MyChurch.exe`.

- Le mélangeur démarre vide. **+ Source audio** ajoute un microphone, le son d'une sortie (son de l'ordinateur), un fichier audio ou vidéo, ou le son de la vidéo projetée depuis Display.
- Chaque bande a deux vumètres, un volume de −60 à +6 dB, **M** (sourdine), le **casque** (écoute : coupée, seule, ou écoute et sortie, comme dans OBS), **Filtres** et les pistes **1 à 6**. Un clic droit sur une bande permet de la renommer, de changer de périphérique ou de fichier, de la passer en mono, de la déplacer ou de la supprimer.
- Filtres d'OBS : réduction du bruit (RNNoise ou Speex), noise gate, compresseur, limiteur. Ils s'appliquent de haut en bas, en direct.
- **● Enregistrer** enregistre une piste en WAV (ou en FLAC avec un `ffmpeg.exe` que vous fournissez) dans `Vidéos\MyChurch`.
- **MyVirtualSound** envoie le mix d'une piste vers un câble audio virtuel : Zoom ou un navigateur le reçoivent comme un micro (« CABLE Output »). VB-CABLE (VB-Audio Software, www.vb-cable.com, donationware) n'est pas inclus : **Paramètres › MyVirtualSound › Installer VB-CABLE** télécharge le paquet officiel à la demande.

Le mixage audio local de la 1.2 bêta et le minuteur de prédication ont été retirés.

## Projection et caméra virtuelle

Le bouton **Projection** propose les moniteurs détectés, leurs dimensions et une sortie en fenêtre. MyChurch projette directement : aucun logiciel externe ni connexion WebSocket n'est nécessaire.

**MyVirtualCam** reçoit automatiquement la sortie en 720p30, Duo compris. Elle s'enregistre une fois avec l'autorisation Windows et reste disponible, avec une image noire, quand MyChurch est fermé. Elle vise les applications compatibles DirectShow et ne transporte pas d'audio.

La capture de fenêtre nécessite une fenêtre ouverte et non réduite. Certains contenus protégés ou accélérés par le GPU ne permettent pas la capture. Les périphériques vidéo physiques nécessitent les pilotes et permissions Windows appropriés.

## Installation et mises à jour

[Téléchargements de la 3.0.0](https://github.com/capmaster2023/Verselight/releases/tag/v3.0.0) :

- `MyChurch-Setup-3.0.0.exe` : installateur Windows.
- `MyChurch-3.0.0-Windows.zip` : version portable.
- `MyChurch.apk` : télécommande Android.

Aucun jeton GitHub n'est intégré dans l'application.

L'installation conserve les chants, Bibles, réglages et téléphones jumelés. Une DLL MyVirtualCam déjà utilisée est renommée avant la copie de sa nouvelle version; le logiciel qui l'utilise charge la nouvelle caméra à sa prochaine ouverture.

Sur le téléphone, installez l'APK par-dessus l'application existante. Le package Android et la signature existante restent conservés. Le PC et le téléphone doivent être sur le même réseau pour la télécommande.

## Licence et code source

MyChurch est un logiciel libre : vous pouvez le redistribuer et le modifier selon les termes de la **GNU General Public License, version 2 ou (à votre choix) toute version ultérieure**, publiée par la Free Software Foundation. Cette licence s'applique parce que MyChurch utilise libobs (OBS Studio), elle-même sous GPL v2 ou ultérieure. Le texte complet est dans [`COPYING`](COPYING), installé avec MyChurch sous le nom `COPYING.txt` (**Paramètres › Code source et licences › Lire la licence GNU GPL**).

- Code source de MyChurch : https://github.com/capmaster2023/Verselight
- Code source d'OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1

Le dossier `obs` livré avec MyChurch contient les bibliothèques du paquet officiel OBS Studio 33.0.1 pour Windows (sans l'interface d'OBS), avec le runtime Microsoft Visual C++ dont elles ont besoin. Les polices de l'interface (Inter, Space Grotesk, Literata, Atkinson Hyperlegible Next, Unbounded) sont sous licence SIL Open Font License 1.1.

## Développement et validation

- `windows/MyVerse.Core` : cœur .NET 8, sans WPF.
- `windows/MyVerse` : interface Windows WPF.
- `windows/MyVerse.Tests` : tests du cœur et du protocole téléphone, dont Duo, et du mélangeur sur la vraie libobs (groupe « Audio OBS »).
- `windows/MyVirtualCam` : composant vidéo DirectShow 32 et 64 bits.
- `android` : télécommande Kotlin / Compose.
- `windows/NOTES-3.0.0.md` : notes de version.

Tests locaux : `dotnet run --project windows/MyVerse.Tests -c Release -- windows/MyVerse/Ressources [groupe]`. Les essais de libobs utilisent le dossier OBS donné par la variable `MYCHURCH_OBS` sous Windows, ou le paquet `obs-studio` sous Linux (avec `xvfb`).

GitHub Actions **Build MyChurch** télécharge le paquet officiel d'OBS 33.0.1 (SHA-256 vérifié), n'en garde que libobs et trois modules (win-wasapi, obs-filters, obs-ffmpeg), vérifie leurs dépendances, compile et teste WPF sur Windows, vérifie les images de projection, les pixels de MyVirtualCam, l'export navigateur local et l'installation, puis construit les APK. La branche `dev` sert à valider. Une avance de `main` publie la version. Les comptes rendus et captures sont copiés dans `ci-logs`.
