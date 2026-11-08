# MyVerse 2.1 — télécommande Android (ex-VerseLight)

VerseLight (Windows, Neutralino) + **VerseLight Remote** (Android) : on tape « jn 3 16 » sur le téléphone, on voit le passage, on touche **Afficher** → il passe sur le projecteur et dans OBS, par VerseLight sur le PC (même Wi-Fi).

## Récupérer les fichiers

GitHub → onglet **Actions** → dernier « Build MyVerse » réussi → en bas, **Artifacts** :

- `MyVerse` : l'APK à installer sur le téléphone (Android 8+).
- `MyVerse-2.1.1-Windows` : le dossier VerseLight complet (VerseLight.exe, resources.neu, extensions\bridge\verselight-bridge.exe, LISEZ-MOI.txt).

Sur un tag `v*` (ex. `v2.1.0`), une **Release** est créée avec l'APK et le zip Windows.

## Mise en place (une seule fois)

1. Sur le PC : décompressez le dossier VerseLight, lancez `MyVerse.exe`. Acceptez la demande Windows (pare-feu, réseau privé). Le Wi-Fi du PC doit être en profil **Privé**.
2. Dans VerseLight : bouton **Téléphone** → un code QR s'affiche.
3. Sur le téléphone : installez l'APK, ouvrez-le, **Scanner le code QR**.

C'est tout : ensuite l'app se reconnecte toute seule à chaque ouverture, même si l'IP du PC change (dernière IP connue → recherche sur le Wi-Fi → autres IP du QR). On peut jumeler plusieurs téléphones ; la fenêtre **Téléphone** permet de les révoquer.

## L'app

- **Bible** : « mt 3 2 », « jn 3 16-18 », « ps 23 » (suggestions de livres), aperçu, **Afficher**, **Retirer**.
- **Chants** : recherche par titre, numéro ou paroles ; toucher une strophe = l'afficher ; bouton **Refrain**.
- Barre du bas : connexion (vert/rouge), ce qui est à l'écran, ‹ Précédent / Suivant ›.
- Réglages : boutons de volume (+ Suivant, − Précédent), garder l'écran allumé, oublier le PC.

## Dépôt

- `android/` — Kotlin + Jetpack Compose, `ca.mepc.verselight.remote`, minSdk 26, targetSdk 35.
- `bridge/` — `verselight-bridge.exe` en Go (bibliothèque standard seulement), lancé par VerseLight comme extension Neutralino.
- `verselight/` — sources de VerseLight (extraites de resources.neu) + `js/remote.js` (télécommande, QR) ; `bin/neutralino-win_x64.exe` est le VerseLight.exe d'origine.
- `.github/workflows/build.yml` — compile tout sur GitHub.

### Signature de l'APK

Sans secrets, l'APK est signé avec `android/app/debug.keystore` (public, mot de passe `android`) : la signature reste la même d'une version à l'autre, donc les mises à jour s'installent sans perdre le jumelage. Pour une clé à vous, ajoutez les secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` (changer de clé oblige à désinstaller l'app une fois).

## Réseau et sécurité

- TCP 47900 (ou le suivant libre jusqu'à 47909) pour le téléphone, UDP 47901 pour la recherche du PC.
- Le QR contient un secret aléatoire ; chaque téléphone en dérive sa propre clé (gardée chiffrée par Android Keystore). À chaque connexion : défi/réponse HMAC-SHA256, le PC prouve aussi son identité. Le secret ne circule jamais sur le réseau. Le code QR n'est valable que tant que la fenêtre Téléphone est ouverte.
- `%APPDATA%\VerseLight\devices.json` : identifiant du PC et téléphones jumelés. Journal : `%APPDATA%\VerseLight\bridge.log`.

### Ça ne se connecte pas ?

Même Wi-Fi des deux côtés, Wi-Fi du PC en **Privé**, pas d'« isolation des clients » sur le routeur, pare-feu autorisé (bouton dans la fenêtre Téléphone si refusé).

## Commandes Termux

```bash
pkg install git gh
gh auth login
git clone https://github.com/capmaster2023/Verselight.git
cd Verselight
git tag v2.1.0 && git push origin v2.1.0     # crée la Release
gh run download --repo capmaster2023/Verselight --name MyVerse --dir ~/storage/downloads
```
