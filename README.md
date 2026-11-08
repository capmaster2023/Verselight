# VerseLight 2.1.0 — Windows et télécommande Android

Une régie Windows pour la Bible Louis Segond 1910 et les chants, avec une télécommande Android en français. La régie reste responsable de l’analyse des références, de la projection, du fichier OBS et de l’œil de la source OBS.

## Télécharger et utiliser

Dans [Actions](https://github.com/capmaster2023/Verselight/actions), ouvrez le dernier workflow **Build VerseLight** réussi puis téléchargez :

- **VerseLight-Remote** : APK release signée, contenant `VerseLight-Remote.apk`.
- **VerseLight-Remote-debug** : APK debug.
- **VerseLight-2.1.0-Windows** : paquet portable Windows contenant `VerseLight-2.1.0-Windows.zip`.

Décompressez le paquet Windows entier. Lancez `VerseLight.exe`, acceptez la demande Windows de pare-feu pour le réseau **privé**, choisissez le projecteur dans **Écran de projection**, puis ouvrez **Téléphone → Nouveau jumelage**. Installez l’APK sur Android 8 ou supérieur, ouvrez **Scanner le code QR**, autorisez la caméra et scannez le code. Le QR expire après 5 minutes ; fermer la fenêtre de jumelage le désactive.

Le téléphone conserve son accès dans EncryptedSharedPreferences avec une clé Android Keystore. Une fermeture de l’app, une indisponibilité du PC ou un changement d’IP ne réinitialisent pas le jumelage. Une révocation depuis la régie bloque immédiatement l’appareil et permet de proposer un nouveau scan.

Tapez `mt 3 2` ou `jn 3 16-18` : l’aperçu apparaît sur le téléphone sans modifier la projection. **Afficher** passe par les mêmes fonctions que la régie. **Retirer** efface l’affichage et désactive l’œil OBS. Les commandes **Noir** et **Logo** sont disponibles dans Réglages. Volume + fait Suivant, volume − fait Précédent ; ces raccourcis et le maintien de l’écran allumé sont désactivables.

## OBS et programme du culte

Dans **Écran de projection**, copiez le chemin proposé pour une source navigateur OBS, cochez **Fichier local** dans OBS et collez ce chemin. Pour allumer/éteindre l’œil, activez obs-websocket 5 dans OBS et indiquez le nom de la source, le port (4455 par défaut) et le mot de passe dans les réglages OBS de VerseLight. La sortie `etat.json` se trouve dans le dossier `VerseLight-OBS` des données utilisateur, comme dans l’application initiale.

Le bouton **Programme** de la régie permet d’ajouter un passage et le chant actuellement ouvert, de réordonner ou de retirer les éléments. La liste est persistante. Sur le téléphone, toucher un élément l’affiche immédiatement. Les changements faits sur le PC sont diffusés aux téléphones.

## Organisation du dépôt

- `android/` : Kotlin, Jetpack Compose Material 3, CameraX, ML Kit embarqué, OkHttp et stockage chiffré. `ca.mepc.verselight.remote`, minSdk 26, compileSdk/targetSdk 35.
- `bridge/` : serveur Go, IPC Neutralino, WebSocket LAN, HMAC, appareils persistants, UDP/mDNS et configuration Windows du pare-feu.
- `verselight/` : sources récupérées du `resources.neu` fourni, configuration Neutralino 6.4.0 et intégration de la télécommande.
- `.github/workflows/build.yml` : tests, APK debug/release, bridge Windows, `neu build`, artefacts et GitHub Release sur tag `v*`.
- `scripts/` : tests de la régie et assemblage du paquet Windows.
- `docs/PROTOCOLE.md` : formats JSON, authentification et événements IPC.
- `docs/LISEZ-MOI.txt` : guide inclus avec Windows.

`android/gradle/wrapper/gradle-wrapper.jar` est réellement inclus. Gradle 8.11.1, Android Gradle Plugin 8.9.2, Kotlin 2.1.20 et JDK 17 sont épinglés. Les bibliothèques QR et les polices sont disponibles localement dans la régie.

## Compilation automatique

Un push sur `main`, une pull request, un lancement manuel ou un tag `v*` démarre le workflow. Les deux jobs Windows et Android tournent sur `ubuntu-latest`. Aucun SDK, Go ni Node n’est nécessaire dans Termux pour lancer cette compilation.

Pour une signature de production, ajoutez les quatre secrets du dépôt dans **Settings → Secrets and variables → Actions** :

| Secret | Contenu |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | Keystore encodé en base64 sur une seule ligne |
| `ANDROID_KEYSTORE_PASSWORD` | Mot de passe du keystore |
| `ANDROID_KEY_ALIAS` | Alias de la clé |
| `ANDROID_KEY_PASSWORD` | Mot de passe de la clé |

En leur absence, les deux variantes utilisent le keystore **de développement public** fourni (`android/debug-signing.jks`, mot de passe `android`, alias `androiddebugkey`). La signature reste stable d’une compilation à l’autre. Remplacer cette signature par une autre nécessite une désinstallation ou une stratégie de migration de signature ; conservez votre keystore de production. Aucun keystore de production n’est inclus dans le dépôt.

Sur un tag `v2.1.0`, une GitHub Release reçoit les APK et le ZIP Windows après réussite des deux jobs. Le bridge est compilé avec `GOOS=windows GOARCH=amd64 CGO_ENABLED=0` en un seul exécutable.

## Commandes Termux : premier envoi depuis le ZIP des sources

Le dépôt GitHub doit déjà exister et être vide. Remplacez le chemin du ZIP par celui du fichier téléchargé.

```bash
pkg update
pkg install git gh unzip
termux-setup-storage
gh auth login
gh auth setup-git
unzip /storage/emulated/0/Download/VerseLight-2.1.0-source.zip -d "$HOME/VerseLight-source"
cd "$HOME/VerseLight-source/VerseLight-2.1.0-source/repository"
git init -b main
git config user.name capmaster2023
git config user.email '136276328+capmaster2023@users.noreply.github.com'
git add .
git commit -m 'VerseLight 2.1.0 : télécommande Android et bridge Windows'
git remote add origin https://github.com/capmaster2023/Verselight.git
git push -u origin main
git tag v2.1.0
git push origin v2.1.0
```

Si les sources ont déjà été envoyées au dépôt, utilisez plutôt :

```bash
pkg install git gh
gh auth login
gh auth setup-git
git clone https://github.com/capmaster2023/Verselight.git
cd Verselight
git tag v2.1.0
git push origin v2.1.0
```

Pour récupérer l’APK sans navigateur :

```bash
gh run list --repo capmaster2023/Verselight --workflow build.yml
gh run download --repo capmaster2023/Verselight --name VerseLight-Remote --dir /storage/emulated/0/Download
```

## Compilation locale facultative

Avec JDK 17 et Android SDK 35 :

```bash
cd android
./gradlew :app:testDebugUnitTest :app:assembleDebug :app:assembleRelease
```

Avec Go 1.23.6+, Node 22 et Python 3 :

```bash
npm ci
npm test
cd bridge
go test -race ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -buildvcs=false -trimpath -ldflags='-s -w -H=windowsgui' -o ../verselight/extensions/bridge/verselight-bridge.exe .
cd ../verselight
npx neu update
npx neu build
cd ..
python3 scripts/package_windows.py
```

## Découverte et dépannage

La reconnexion essaie la dernière IPv4 connue, puis UDP et mDNS pour le **même pcId**, puis les adresses du QR. Elle recommence avec un délai exponentiel plafonné à 5 secondes entre les cycles ; la découverte et les essais ont leurs propres délais. Un callback Wi-Fi relance immédiatement la recherche. Les connexions inactives sont vérifiées par ping WebSocket toutes les 10 secondes. Les commandes faites hors ligne sont refusées et ne sont pas rejouées plus tard.

Le bridge utilise TCP 47900, ou le prochain port libre jusqu’à 47999. UDP 47901 répond à `VERSELIGHT_DISCOVER`. Le service mDNS est `_verselight._tcp.local.` et annonce `pcId` dans ses attributs. Les annonces mDNS sont renouvelées lorsque les interfaces IPv4 changent.

Au premier lancement, le bridge ajoute en une seule élévation UAC des règles `netsh advfirewall` pour TCP 47900–47999, UDP 47901 et UDP 5353, limitées à son exécutable, au profil privé et au sous-réseau local. La plage TCP permet de fonctionner si 47900 est occupé. Un refus est mémorisé et expliqué dans **Téléphone** ; il n’est jamais redemandé automatiquement. **Réessayer le pare-feu** est une action volontaire.

En cas d’échec, vérifiez le profil réseau privé du PC, le même réseau sur les deux appareils, le pare-feu, les VPN et l’isolation des clients Wi-Fi. L’adresse IPv4:port de **Téléphone** peut être saisie dans **Réglages** sur Android, sans nouveau jumelage.

`%APPDATA%\VerseLight\devices.json` conserve le pcId stable, les clés dérivées par appareil et les révocations. Les entrées révoquées restent mémorisées pour signer un refus vérifiable ; elles ne peuvent plus commander ni recevoir l’état. Le fichier est écrit par remplacement et le dossier est restreint à l’utilisateur Windows et SYSTEM. Ne le supprimez pas si vous voulez conserver les jumelages. Le journal technique est `%APPDATA%\VerseLight\bridge.log` et ne contient ni secrets ni références bibliques.

Le QR contient un secret aléatoire de 32 octets qui ne circule qu’au scan. Chaque appareil en dérive une clé propre. Le défi et les preuves HMAC utilisent un nonce neuf ; le PC prouve aussi son identité au téléphone. Le WebSocket LAN transporte les textes et commandes sans chiffrement du transport : utilisez un réseau local de confiance, sans redirection de ports Internet. L’IPC Neutralino est limité à la boucle locale et le bridge s’arrête à sa fermeture.

## Vérification sur le matériel

Les tests automatiques vérifient les références et textes réels, l’absence de projection lors d’un aperçu, les commandes communes, le programme, les preuves HMAC, plusieurs appareils, la reconnexion, la révocation persistante et la découverte UDP. La validation de bout en bout sur un téléphone physique, un PC Windows, un projecteur et OBS reste à faire : jumelage, redémarrage des deux apps, changement d’IP du PC, deux téléphones et révocation.

## Contenus fournis

La Bible LSG 1910, les chants, les polices, PDF.js et le code de la régie proviennent de l’archive VerseLight fournie. Les mentions et licences existantes sont conservées. Le générateur QR est distribué sous licence MIT ; son fichier de licence est inclus. Cette livraison n’ajoute aucune licence globale aux chants importés ni au code d’origine.
