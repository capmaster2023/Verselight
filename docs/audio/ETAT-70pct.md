# Point de contrôle actualisé — environ 70 %

Cette archive remplace le point intermédiaire à 50 %. Sources complètes, au stade actuel du travail. Ce n’est pas encore le paquet Windows final.

Base inchangée : v1.1.2, commit 007aff5c1b668567defdc98c53fecca6ff315966. Aucun envoi GitHub.

Depuis le point à 50 % :
- surveillance des erreurs d’enregistrement même si la fenêtre audio est fermée ;
- reconnexion et réévaluation des périphériques par défaut ;
- finalisation lors de la fermeture ;
- aperçu de balance distinct de la configuration permanente ;
- partage des caméras entre Display, Duo et l’enregistrement ;
- ajout des vidéos dans Display et de leur source audio dans le mixeur ;
- synchronisation des vidéos propres au profil avec leur média audio sélectionné ;
- extraction du moteur d’enregistrement dans le cœur testable ;
- tests réels de la file WAV et tests d’encodage FLAC/MKV avec FFmpeg et décodage complet ;
- contrôle réel des codecs, des fréquences, des canaux et de la durée des fichiers avec FFprobe ;
- utilisation de NAudio.Wasapi et NAudio.Core seulement, sans paquet ASIO.

Compilation Release WPF : zéro erreur et zéro avertissement.
Suite de régression déjà passée : 345 vérifications ; 15 vérifications supplémentaires d’enregistrement passées séparément. La suite globale actualisée est en cours au moment de ce point.

Limites d’essai : pas de Windows, pas d’interfaces Focusrite/Behringer et pas de Zoom dans cet environnement. Le transport des flux FFmpeg par pipes Windows n’a pas été exécuté ici. Sous Linux, l’encodage est testé sur de vrais fichiers d’entrée, avec exactement les arguments de l’application.

Reste : revue finale, approfondissement des tests, guide, notices des dépendances et paquet portable Windows avec l’exécutable compilé.

Compiler : `dotnet build windows/MyVerse -c Release` (SDK .NET 8).
Tester : `dotnet run --project windows/MyVerse.Tests -c Release -- windows/MyVerse/Ressources`.
Tests FFmpeg facultatifs : définir MYCHURCH_FFMPEG avec le chemin absolu de l’exécutable, et placer ffprobe à côté.
