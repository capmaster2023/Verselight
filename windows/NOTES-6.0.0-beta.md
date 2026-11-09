# MyChurch 6.0.0 bêta

- Nouveau nom **MyChurch** et nouveau logo sur Windows et Android.
- **Display** remplace l'onglet Annonces. Les anciens textes restent disponibles.
- Double-clic sur le titre d'un Display ou d'un chant : envoi immédiat à la sortie en direct. Double-clic dans la liste ou sur le titre de l'éditeur Display fonctionne aussi.
- **Périphérique de capture vidéo** : sélection d'une webcam ou d'une carte de capture USB/HDMI compatible DirectShow, comme la catégorie équivalente d'OBS. Sortie visée 1280 × 720 / 30 ips, selon le périphérique.
- Import de plusieurs images (copiées dans les données de MyChurch), capture d'une fenêtre choisie, ajout de ces sources au déroulé du culte et affichage dans la projection / source navigateur OBS.
- **MyVirtualCam** : périphérique de caméra virtuel DirectShow 64 et 32 bits, indépendant d'OBS. L'installateur demande l'autorisation Windows pour l'enregistrer. Après installation, il reste dans la liste des caméras. La sortie de MyChurch y est envoyée automatiquement, sans bouton Démarrer. MyChurch fermé, aucune source affichée ou sortie interrompue : vidéo noire, sans conserver la dernière image.
- APK **MyChurch** : onglet Display, liste des sources du PC, Afficher en direct, état courant, Précédent / Suivant / Retirer. Identité Android, signature et jumelage conservés.
- Mises à jour : les utilisateurs de la bêta peuvent consulter les publications bêta; une version finale de même numéro remplace la bêta.

## Installation

PC : lancer `MyChurch-Setup-6.0.0-beta.exe` et accepter l'installation MyVirtualCam. Version portable : ouvrir Display puis cliquer MyVirtualCam pour enregistrer le périphérique une fois. Fermer puis rouvrir les logiciels vidéo déjà ouverts pour qu'ils découvrent la caméra.

Téléphone : installer `MyChurch.apk` par-dessus l'application actuelle. Pour remplacer une installation signée avec une autre clé, la même clé doit être fournie à GitHub Actions.

## Limites de la bêta

MyVirtualCam vise les applications compatibles DirectShow sous Windows (dont OBS); la compatibilité des applications qui n'acceptent que Media Foundation n'est pas garantie. La vidéo virtuelle ne transporte pas d'audio. Les permissions de caméra Windows s'appliquent aux webcams et cartes de capture. La capture de fenêtre nécessite une fenêtre ouverte et non réduite; certains contenus protégés ou rendus par le GPU peuvent être noirs. Les GIF importés sont des images fixes. Le flux OBS par fichier JPEG est limité à environ 10 ips, alors que MyVirtualCam publie à 30 ips et la capture de fenêtre vise 15 ips.

## Vérification

Tests du cœur et du protocole téléphone; compilation WPF; tests Windows des pixels d'une image Display, du double-clic, de la sortie OBS et de la mémoire vidéo MyVirtualCam; test DirectShow 32 et 64 bits avec un vrai graphe recevant des images, logiciel fermé puis image synthétique. Aucun périphérique USB physique n'est disponible sur le serveur de compilation : webcams et cartes HDMI doivent encore être essayées sur le PC de l'utilisateur.
