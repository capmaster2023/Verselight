# MyVerse 5 — projection Bible et chants (logiciel Windows natif) + télécommande Android

## Nouveautés 5.0 : design « Pupitre » (clair) et agenda

- **Agenda** (comme VideoPsalm) : un agenda par culte, préparé à l'avance, avec des chants de tous les recueils, des passages de la Bible et des textes (annonces, prières). « Suivant » passe d'un élément à l'autre.
- **Barre de commande** (Ctrl K) : « jean 3 16 », « chant 45 », « minuteur 35 »… Entrée affiche, Tab ajoute à l'agenda.
- **Notes de prédication** : collez vos notes, les versets sont trouvés tout seuls (F1 à F9 pour les afficher).
- **L'assemblée suit sur son téléphone** : un code QR ouvre une page (même Wi-Fi) où le texte arrive en direct ; chacun choisit sa langue.
- Écran noir (touche B), minuteur de prédication, interface claire et minimale.


MyVerse (Windows) + l'app **MyVerse** (Android) : on tape « jn 3 16 » sur le PC ou sur le téléphone, on voit le passage, on touche **Afficher** → il passe sur le projecteur et dans OBS (même Wi-Fi).

## Nouveautés 4.0 : un vrai logiciel Windows

- **Programme Windows natif** (C# / WPF, .NET 8) : plus de page web, plus de WebView2, plus de module « bridge » à côté. Un seul programme, `MyVerse.exe`, qui démarre vite et fonctionne sans Internet.
- **Les mêmes fonctions** que la 3.x : interface « Studio » (Bibles et livres à gauche, texte au centre, l'écran à droite), recherche floue dans toutes les Bibles, versets bilingues, chants rangés tout seuls, ✂ Découper, imports (VideoPsalm, OpenLyrics, OpenSong, ChordPro, SongSelect, ProPresenter 6, PDF, texte, site web), OBS, télécommande Android.
- **Affichage par OBS** (bouton OBS › Piloter OBS) : texte à la plus grande taille possible, lignes équilibrées ; l'aperçu de la régie montre exactement ce qui est à l'écran. (4.0.1 : bouton « Écran de projection » retiré.)
- **Vos données sont reprises toutes seules** au premier démarrage : chants, réglages, Bibles importées, versets découpés (copie faite par la 3.0.4, ou lecture directe du profil de la 3.x) et téléphones jumelés (rien à rescanner).
- **Téléphone** : même protocole que la 3.x, l'app Android n'a pas besoin d'être mise à jour.

## Nouveautés 3.0

- **Nouvelle interface (3.0.2)** : à gauche la liste des Bibles (un clic = elle s'affiche, « 2e » = versets bilingues, « ＋ Importer une Bible ») et les livres ; au centre le chapitre ou le chant ; à droite ce qui est à l'écran, le suivant, Afficher, Précédent / Suivant, Retirer, le fond et la taille. La grande recherche en haut cherche dans la Bible ou dans les chants selon l'onglet.

- **Chants rangés tout seuls** : collez un chant complet, même pas arrangé (Chants › Nouveau, ou Importer › Coller) : MyVerse le coupe en diapositives lisibles de 3, 4 ou 5 lignes, retire les accords et reconnaît le refrain. Aperçu avant d'enregistrer.
- **Suivant** : le verset suivant (Bible) ou le couplet suivant (chants) est affiché en entier sous l'aperçu, sur le PC et sur le téléphone.
- **Découper une diapositive** : bouton ✂ (ou double-clic sur une strophe) : appuyez sur **Entrée** devant une ligne pour commencer une nouvelle diapositive (2 à 5 morceaux). Un long verset se découpe pareil ; Suivant passe par chaque morceau.
- **Vos Bibles** : bouton **Bibles** › Importer (Zefania XML, OSIS, USFX, JSON ou texte « Livre 1:1 … ») — kirundi, swahili, anglais… La King James (anglais) est incluse. Choisissez la version principale.
- **Versets bilingues** : une 2e version s'affiche sous le verset, plus petite (ex. LSG · KJV, ou LSG · votre Bible en kirundi).
- **Vos recueils** : Chants › Importer (OpenLP, OpenSong, VideoPsalm, EasyWorship, PowerPoint, texte, PDF…) en donnant le nom du recueil, ou collez tout un recueil en texte.
- **Recherche par mots-clés dans toutes les Bibles** (version principale, 2e version, Bibles importées) : même mal écrit (« kar dieu a tan aimer le monde » → Jean 3:16), mots surlignés, version indiquée, résultats classés.
- **Détecteur de versets** (téléphone) : interrupteur dans l'onglet Bible. L'app écoute le prédicateur (« Jean chapitre trois verset seize », « Romains 8 28 », « au verset 17 »…) et propose le verset avec un bouton **Afficher**. App fermée ou écran éteint : une notification « Verset cité » avec **Afficher**.

## Récupérer les fichiers

GitHub → onglet **Actions** → dernier « Build MyVerse » réussi → en bas, **Artifacts** :

- `MyVerse` : l'APK à installer sur le téléphone (Android 8+).
- `windows-zip` : l'installateur `MyVerse-Setup-4.0.0.exe` et le dossier portable.

Le plus simple : onglet **Releases** → dernière version → `MyVerse-Setup-x.y.z.exe` (PC) et `MyVerse.apk` (téléphone).

## Mise en place (une seule fois)

1. Sur le PC : lancez `MyVerse-Setup-x.y.z.exe` (ou décompressez le dossier portable et lancez `MyVerse.exe`). Acceptez la demande Windows (pare-feu).
2. Dans MyVerse : bouton **Téléphone** → un code QR s'affiche.
3. Sur le téléphone : installez l'APK, ouvrez-le, **Scanner le code QR**.

C'est tout : ensuite l'app se reconnecte toute seule à chaque ouverture, même si l'IP du PC change (dernière IP connue → recherche sur le Wi-Fi → autres IP du QR). On peut jumeler plusieurs téléphones ; la fenêtre **Téléphone** permet de les révoquer.

## L'app

- **Bible** : « mt 3 2 », « jn 3 16-18 », « ps 23 » (suggestions de livres), ou des mots (« charité patiente », même mal écrits), aperçu, **Afficher**, **Retirer**. Détecteur de versets en haut de l'onglet.
- **Chants** : recherche par titre, numéro ou paroles ; toucher une strophe = l'afficher ; bouton **Refrain**.
- Barre du bas : connexion au PC (vert/rouge), ce qui est à l'écran, ‹ Précédent / Suivant ›. MyVerse doit être ouvert sur le PC.
- Réglages : boutons de volume (+ Suivant, − Précédent), garder l'écran allumé, couper le bip du micro, journal d'erreurs, oublier le PC.

### Détecteur de versets

Interrupteur **Détecteur de versets** (onglet Bible) : autorisez le micro et les notifications. MyVerse doit être ouvert sur le PC pour afficher. Il utilise la reconnaissance vocale du téléphone (application Google, en français) et ne garde aucun enregistrement. Pour limiter les fausses alertes, « Jean 3 » seul (prénom courant) n'est proposé que si le mot « chapitre » est dit ou qu'un verset suit.

## Dépôt

- `windows/` — **MyVerse 4** (C# .NET 8) :
  - `MyVerse.Core/` : tout sauf l'interface (Bibles, recherche floue, imports de Bibles et de chants, rangement automatique, régie, serveur des téléphones, OBS, mises à jour, reprise des données de la 3.x). Sans dépendance : se compile et se teste partout.
  - `MyVerse/` : l'interface WPF (`MainWindow`, fenêtre de projection `EcranWindow` / `Rendu`, dialogues), polices incluses, `Ressources/` (Louis Segond, King James, chants de l'église, page OBS).
  - `MyVerse.Tests/` : 190+ vérifications du cœur (`dotnet run --project windows/MyVerse.Tests -- windows/MyVerse/Ressources`).
  - `installer/MyVerse.nsi` : l'installateur.
- `android/` — Kotlin + Jetpack Compose, `ca.mepc.verselight.remote`, minSdk 26, targetSdk 35.
- `verselight/` et `bridge/` — l'ancienne version 3.x (HTML / Neutralino et module réseau en Go), gardées pour mémoire.
- `.github/workflows/build.yml` — compile tout sur GitHub (Windows : tests, compilation, test automatique de l'interface avec captures d'écran dans la branche `ci-logs`).

### Signature de l'APK

Sans secrets, l'APK est signé avec `android/app/debug.keystore` (public, mot de passe `android`) : la signature reste la même d'une version à l'autre, donc les mises à jour s'installent sans perdre le jumelage. Pour une clé à vous, ajoutez les secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` (changer de clé oblige à désinstaller l'app une fois).

## Réseau et sécurité

- TCP 47900 (ou le suivant libre jusqu'à 47909) pour le téléphone, UDP 47901 pour la recherche du PC.
- Le QR contient un secret aléatoire ; chaque téléphone en dérive sa propre clé (gardée chiffrée par Android Keystore). À chaque connexion : défi/réponse HMAC-SHA256, le PC prouve aussi son identité. Le secret ne circule jamais sur le réseau. Le code QR n'est valable que tant que la fenêtre Téléphone est ouverte.
- `%APPDATA%\VerseLight\devices.json` : identifiant du PC et téléphones jumelés. Journal : `%APPDATA%\VerseLight\journal-myverse.txt` (aussi dans Outils › Journal d'erreurs).

### Ça ne se connecte pas ?

Même Wi-Fi des deux côtés, Wi-Fi du PC en **Privé**, pas d'« isolation des clients » sur le routeur, pare-feu autorisé (bouton dans la fenêtre Téléphone si refusé).

## Commandes Termux

```bash
pkg install git gh
gh auth login
git clone https://github.com/capmaster2023/Verselight.git
cd Verselight
git tag v4.0.0 && git push origin v4.0.0     # crée la Release
gh run download --repo capmaster2023/Verselight --name MyVerse --dir ~/storage/downloads
```

## Installateur et mises à jour

- **Windows :** `MyVerse-Setup-x.y.z.exe` installe MyVerse dans `%LOCALAPPDATA%\Programs\MyVerse` (sans droits administrateur), crée les raccourcis Bureau et menu Démarrer et ajoute MyVerse dans Paramètres › Applications (pour le désinstaller). Pas besoin de désinstaller pour mettre à jour : MyVerse vérifie au démarrage s'il existe une nouvelle version et affiche **Mettre à jour** à côté de son nom ; un clic télécharge l'installateur, l'installe par-dessus et rouvre MyVerse. Cliquer sur le numéro de version (ex. v4.0.0) vérifie tout de suite. Chants, réglages et téléphones jumelés sont gardés.
- **Android :** l'app affiche **Mise à jour disponible : télécharger** ; l'APK s'installe par-dessus l'ancienne (même signature), le jumelage est gardé.
- Chaque compilation réussie sur `main` publie (ou met à jour) la Release `v<version>` avec l'installateur, le dossier portable et l'APK. Pour sortir une nouvelle version, changez le numéro dans le fichier `VERSION` (et `<Version>` dans `windows/MyVerse/MyVerse.csproj`). La branche `dev` compile tout sans rien publier.
- La vérification automatique passe par l'API publique de GitHub : **elle ne fonctionne que si le dépôt est public**. Avec un dépôt privé, téléchargez les nouvelles versions dans Releases et lancez l'installateur (il met à jour par-dessus, sans désinstaller).
