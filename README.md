# MyVerse 3.0 — projection Bible et chants (Windows) + télécommande Android

MyVerse (Windows, Neutralino) + l'app **MyVerse** (Android) : on tape « jn 3 16 » sur le téléphone, on voit le passage, on touche **Afficher** → il passe sur le projecteur et dans OBS (même Wi-Fi).

## Nouveautés 3.0

- **Chants rangés tout seuls** : collez un chant complet, même pas arrangé (Chants › Nouveau, ou Importer › Coller) : MyVerse le coupe en diapositives lisibles de 3, 4 ou 5 lignes, retire les accords et reconnaît le refrain. Aperçu avant d'enregistrer.
- **Suivant** : le verset suivant (Bible) ou le couplet suivant (chants) est affiché en entier sous l'aperçu, sur le PC et sur le téléphone.
- **Découper une diapositive** : bouton ✂ (ou double-clic sur une strophe) : appuyez sur **Entrée** devant une ligne pour commencer une nouvelle diapositive (2 à 5 morceaux). Un long verset se découpe pareil ; Suivant passe par chaque morceau.
- **Vos Bibles** : bouton **Bibles** › Importer (Zefania XML, OSIS, USFX, JSON ou texte « Livre 1:1 … ») — kirundi, swahili, anglais… La King James (anglais) est incluse. Choisissez la version principale.
- **Versets bilingues** : une 2e version s'affiche sous le verset, plus petite (ex. LSG · KJV, ou LSG · votre Bible en kirundi).
- **Vos recueils** : Chants › Importer (OpenLP, OpenSong, VideoPsalm, EasyWorship, PowerPoint, texte, PDF…) en donnant le nom du recueil, ou collez tout un recueil en texte.
- **Recherche par mots-clés dans toutes les Bibles** (version principale, 2e version, Bibles importées) : même mal écrit (« kar dieu a tan aimer le monde » → Jean 3:16), mots surlignés, version indiquée, résultats classés.
- **Téléphone sans le PC** : jumelez une fois ; ensuite, si MyVerse est fermé sur le PC, l'app se connecte **directement à OBS** et affiche versets et chants (Bible Louis Segond incluse dans l'app ; chants, Bibles importées, thème et réglages OBS copiés depuis le PC à chaque connexion).
- **Détecteur de versets** (téléphone) : interrupteur dans l'onglet Bible. L'app écoute le prédicateur (« Jean chapitre trois verset seize », « Romains 8 28 », « au verset 17 »…) et propose le verset avec un bouton **Afficher**. App fermée ou écran éteint : une notification « Verset cité » avec **Afficher**.

## Récupérer les fichiers

GitHub → onglet **Actions** → dernier « Build MyVerse » réussi → en bas, **Artifacts** :

- `MyVerse` : l'APK à installer sur le téléphone (Android 8+).
- `windows-zip` : l'installateur `MyVerse-Setup-3.0.0.exe` et le dossier portable.

Le plus simple : onglet **Releases** → dernière version → `MyVerse-Setup-x.y.z.exe` (PC) et `MyVerse.apk` (téléphone).

## Mise en place (une seule fois)

1. Sur le PC : décompressez le dossier VerseLight, lancez `MyVerse.exe`. Acceptez la demande Windows (pare-feu, réseau privé). Le Wi-Fi du PC doit être en profil **Privé**.
2. Dans VerseLight : bouton **Téléphone** → un code QR s'affiche.
3. Sur le téléphone : installez l'APK, ouvrez-le, **Scanner le code QR**.

C'est tout : ensuite l'app se reconnecte toute seule à chaque ouverture, même si l'IP du PC change (dernière IP connue → recherche sur le Wi-Fi → autres IP du QR). On peut jumeler plusieurs téléphones ; la fenêtre **Téléphone** permet de les révoquer.

## L'app

- **Bible** : « mt 3 2 », « jn 3 16-18 », « ps 23 » (suggestions de livres), ou des mots (« charité patiente », même mal écrits), aperçu, **Afficher**, **Retirer**. Détecteur de versets en haut de l'onglet.
- **Chants** : recherche par titre, numéro ou paroles ; toucher une strophe = l'afficher ; bouton **Refrain**.
- Barre du bas : connexion (vert = PC ou OBS direct, rouge = rien), ce qui est à l'écran, ‹ Précédent / Suivant ›.
- Réglages : boutons de volume (+ Suivant, − Précédent), garder l'écran allumé, piloter OBS quand MyVerse est fermé, couper le bip du micro, journal d'erreurs, oublier le PC.

### OBS sans le PC (mode direct)

1. Dans MyVerse sur le PC : bouton **OBS** → activez, port 4455, mot de passe du serveur WebSocket d'OBS (OBS › Outils › Paramètres du serveur WebSocket).
2. Connectez le téléphone au PC au moins une fois (il reçoit alors les réglages OBS, chiffrés, ainsi que les chants et les Bibles).
3. Ensuite, si MyVerse est fermé : la barre du bas indique **OBS direct** ; Afficher place la source « MyVerse (temporaire) » au premier plan de la scène en cours, Retirer la supprime et la scène redevient comme avant.

Le pare-feu doit laisser passer OBS (TCP 4455) : l'installateur ajoute la règle « MyVerse OBS (TCP) ».

### Détecteur de versets

Interrupteur **Détecteur de versets** (onglet Bible) : autorisez le micro et les notifications. Il utilise la reconnaissance vocale du téléphone (application Google, en français) et ne garde aucun enregistrement. Pour limiter les fausses alertes, « Jean 3 » seul (prénom courant) n'est proposé que si le mot « chapitre » est dit ou qu'un verset suit.

## Dépôt

- `android/` — Kotlin + Jetpack Compose, `ca.mepc.verselight.remote`, minSdk 26, targetSdk 35.
- `bridge/` — `verselight-bridge.exe` en Go (bibliothèque standard seulement), lancé par VerseLight comme extension Neutralino.
- `verselight/` — sources de VerseLight (extraites de resources.neu) + `js/remote.js` (télécommande, QR) ; `bin/neutralino-win_x64.exe` est le VerseLight.exe d'origine.
- `.github/workflows/build.yml` — compile tout sur GitHub.

### Signature de l'APK

Sans secrets, l'APK est signé avec `android/app/debug.keystore` (public, mot de passe `android`) : la signature reste la même d'une version à l'autre, donc les mises à jour s'installent sans perdre le jumelage. Pour une clé à vous, ajoutez les secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` (changer de clé oblige à désinstaller l'app une fois).

## Réseau et sécurité

- TCP 47900 (ou le suivant libre jusqu'à 47909) pour le téléphone, UDP 47901 pour la recherche du PC, TCP 4455 pour OBS (mode direct).
- Le mot de passe OBS n'est jamais envoyé en clair : le PC le chiffre (AES-256-GCM) avec la clé propre à chaque téléphone, qui le garde chiffré par Android Keystore.
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
git tag v3.0.0 && git push origin v3.0.0     # crée la Release
gh run download --repo capmaster2023/Verselight --name MyVerse --dir ~/storage/downloads
```

## Installateur et mises à jour

- **Windows :** `MyVerse-Setup-x.y.z.exe` installe MyVerse dans `%LOCALAPPDATA%\Programs\MyVerse` (sans droits administrateur), crée les raccourcis Bureau et menu Démarrer et ajoute MyVerse dans Paramètres › Applications (pour le désinstaller). Pas besoin de désinstaller pour mettre à jour : MyVerse vérifie au démarrage s'il existe une nouvelle version et affiche **Mettre à jour** à côté de son nom ; un clic télécharge l'installateur, l'installe par-dessus et rouvre MyVerse. Cliquer sur le numéro de version (ex. v2.1.3) vérifie tout de suite. Chants, réglages et téléphones jumelés sont gardés.
- **Android :** l'app affiche **Mise à jour disponible : télécharger** ; l'APK s'installe par-dessus l'ancienne (même signature), le jumelage est gardé.
- Chaque compilation réussie sur `main` publie (ou met à jour) la Release `v<version>` avec l'installateur, le dossier portable et l'APK. Pour sortir une nouvelle version, augmentez `version` dans `verselight/neutralino.config.json`. La branche `dev` compile tout sans rien publier.
- La vérification automatique passe par l'API publique de GitHub : **elle ne fonctionne que si le dépôt est public**. Avec un dépôt privé, téléchargez les nouvelles versions dans Releases et lancez l'installateur (il met à jour par-dessus, sans désinstaller).
