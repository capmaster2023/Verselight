# MyChurch 1.0.2 bêta

Logiciel Windows autonome de projection pour l'église, avec télécommande Android. Bible, chants et sources Display partent directement sur l'écran choisi dans **Projection**.

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

## Projection et caméra virtuelle

Le bouton **Projection** propose les moniteurs détectés, leurs dimensions et une sortie en fenêtre. MyChurch projette directement : aucun logiciel externe ni connexion WebSocket n'est nécessaire.

**MyVirtualCam** reçoit automatiquement la sortie en 720p30, Duo compris. Elle s'enregistre une fois avec l'autorisation Windows et reste disponible, avec une image noire, quand MyChurch est fermé. Elle vise les applications compatibles DirectShow et ne transporte pas d'audio.

La capture de fenêtre nécessite une fenêtre ouverte et non réduite. Certains contenus protégés ou accélérés par le GPU ne permettent pas la capture. Les périphériques vidéo physiques nécessitent les pilotes et permissions Windows appropriés.

## Installation et mises à jour

[Téléchargements de la 1.0.2 bêta](https://github.com/capmaster2023/Verselight/releases/tag/v1.0.2-beta) :

- `MyChurch-Setup-1.0.2-beta.exe` : installateur Windows.
- `MyChurch-1.0.2-beta-Windows.zip` : version portable.
- `MyChurch.apk` : télécommande Android.

Le dépôt est privé : connectez-vous à GitHub avec un compte autorisé pour télécharger les fichiers. Aucun jeton GitHub n'est intégré dans l'application.

L'installation conserve les chants, Bibles, réglages et téléphones jumelés. Une DLL MyVirtualCam déjà utilisée est renommée avant la copie de sa nouvelle version; le logiciel qui l'utilise charge la nouvelle caméra à sa prochaine ouverture.

Sur le téléphone, installez l'APK par-dessus l'application existante. Le package Android et la signature existante restent conservés. Le PC et le téléphone doivent être sur le même réseau pour la télécommande.

## Développement et validation

- `windows/MyVerse.Core` : cœur .NET 8, sans WPF.
- `windows/MyVerse` : interface Windows WPF.
- `windows/MyVerse.Tests` : tests du cœur et du protocole téléphone, dont Duo.
- `windows/MyVirtualCam` : composant vidéo DirectShow 32 et 64 bits.
- `android` : télécommande Kotlin / Compose.
- `windows/NOTES-1.0.2-beta.md` : notes de version.

Tests locaux : `cd windows/MyVerse.Tests`, puis `dotnet build -c Release` et `dotnet bin/Release/net8.0/MyVerse.Tests.dll`.

GitHub Actions **Build MyChurch** compile et teste WPF sur Windows, vérifie les images de projection, les pixels de MyVirtualCam, l'export navigateur local et l'installation, puis construit les APK. La branche `dev` sert à valider. Une avance de `main` publie la version. Les comptes rendus et captures sont copiés dans `ci-logs`.
