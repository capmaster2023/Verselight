# MyChurch 1.2.0 bêta — mixeur audio et enregistrement

**Bêta : jamais essayée sur du vrai matériel. Ne pas l'utiliser pour un culte avant de l'avoir testée.**

- Mixeur audio : plusieurs interfaces, canaux séparés, sources logiques (Chanteur et Pasteur sur la même entrée), volume, sourdine, balance, vumètres.
- Bus Salle, Zoom et Enregistrement indépendants ; envoi vers un périphérique virtuel (VB-CABLE) ; écoute du mix Zoom.
- Filtres par source : gain, passe-haut, passe-bas, égaliseur, gate, expandeur, compresseur, sidechain, limiteur, réduction de bruit.
- Modes de culte, balance guidée, 4 boutons d'enregistrement (WAV intégré ; FLAC et vidéo avec un ffmpeg.exe fourni par vous).

Corrections de cette version : sorties audio qui suivent l'horloge de la carte son (plus de blocs jetés), limiteur à anticipation, filtres qui gardent leur état quand on bouge un curseur, coupure sans clic au démarrage d'un enregistrement, garde-fou contre les valeurs invalides, minuterie audio à 1 ms.

À essayer sur votre PC : Focusrite et Behringer ensemble pendant 30 minutes, VB-CABLE vers Zoom, débranchement à chaud, enregistrement d'une heure.
