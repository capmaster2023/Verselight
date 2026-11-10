# Point de contrôle intermédiaire — MyChurch audio

Base exacte : tag v1.1.2, commit 007aff5c1b668567defdc98c53fecca6ff315966.
Branches main et dev à ce commit au moment de la lecture. Aucun envoi GitHub.

Cette archive contient les sources complètes à ce stade, pas une version finale validée sur Windows.

Déjà ajouté : moteur audio flottant à 48 kHz, distribution des canaux physiques aux sources logiques, capture WASAPI et loopback avec NAudio, bus indépendants, DSP ordonné, sidechain, suppression spectrale locale du bruit, groupes d’exclusion, modes, restrictions temporaires des profils, interface WPF simple/avancée, configuration des quatre profils, écriture RF64 PCM 24 bits, chaîne FFmpeg externe pour FLAC/MKV, composition hors écran, partage des caméras.

Vérifications de ce point : compilation WPF sur Linux avec ciblage Windows (zéro erreur, zéro avertissement), compilation du cœur, 33 vérifications audio synthétiques réussies.

La suite du travail porte sur la revue des changements, les régressions, la reconnexion, l’intégration des médias Display, le cycle de fermeture, les validations de fichiers et la documentation. Le fonctionnement matériel Windows n’a pas été exécuté ici : ne pas considérer cette archive comme prête pour un culte.

Compilation : installer le SDK .NET 8 puis `dotnet build windows/MyVerse -c Release`.
Tests : `dotnet run --project windows/MyVerse.Tests -- windows/MyVerse/Ressources`.
Ne pas déclencher de workflow ou de publication pour compiler cette archive.
