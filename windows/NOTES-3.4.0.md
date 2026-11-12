# MyChurch 3.4.0 : nouvelle app mobile et MyCam (le téléphone devient une caméra)

**Pas encore essayé sur du vrai matériel d'église (projecteur, téléphones, Wi-Fi de l'église). Faites un essai complet avant de l'utiliser pendant un culte.**

## L'app mobile refaite à zéro

Nouvelle app Android (MyChurch.apk), ivoire, bleu nuit et or comme le logiciel, avec 5 onglets en bas :

- **Régie** : l'écran de l'église en direct (aperçu avec l'habillage), la diapositive suivante, Précédent / Suivant, **Noir**, **Retirer**, et le choix de l'**écran par défaut** (Noir, Fond seul ou un écran principal du déroulé). Une barre de commande : tapez ou dictez « jn 3 16 », « chant 154 », « noir », « retirer ». Le détecteur de versets est là aussi.
- **Bible** : référence ou mots ; touchez les versets à afficher, puis **Afficher** ou **au culte** (ajout au déroulé).
- **Chants** : recherche, strophes en tuiles (rouge : à l'écran, orange : suivante), **Refrain**, **au culte**.
- **Culte** : le déroulé du PC (heure, type, À L'ÉCRAN, PAR DÉFAUT, étapes passées) ; un toucher l'affiche. L'onglet **Display** liste les sources du PC.
- **MyCam** : voir plus bas.

La barre « À l'écran » (Précédent, Suivant) reste en bas de chaque page. Les boutons de volume font toujours Suivant et Précédent (sauf dans MyCam). Le jumelage par code QR ne change pas : les téléphones déjà jumelés restent jumelés.

## MyCam : le téléphone devient une caméra du PC

Comme DroidCam, mais intégré à MyChurch :

1. Sur le téléphone : onglet **MyCam**, cadrez, touchez **Diffuser**.
2. Sur le PC : **Display › Ajouter › Téléphone MyCam**. La source s'utilise comme une caméra USB : en direct, dans le déroulé, comme écran principal, dans le Duo.

Sur le téléphone : **témoin** rouge quand la caméra est à l'écran (avec le temps), orange quand elle passe ensuite, vibration au passage à l'écran ; zoom (0,5× à 3×, ou deux doigts), mise au point au toucher, exposition, lampe, caméra avant ou arrière, grille, **plein écran à l'horizontale**. L'écran du téléphone s'assombrit tout seul pendant la diffusion (la caméra continue).

**Réglages MyCam** : nom de la source dans MyChurch, 720p ou 1080p, 15, 24 ou 30 images par seconde, Wi-Fi ou **câble USB** (partage de connexion USB du téléphone).

Sur le PC : la tuile MyCam montre l'image du téléphone même hors de l'écran. Clic droit › **Message au caméraman** (« Plan serré sur le pasteur », « Plan large »… ou un message libre) : le message s'affiche sur le téléphone, et « Vu » revient sur le PC.

Les images passent par la connexion déjà jumelée et authentifiée du téléphone : aucun port de plus à ouvrir dans le pare-feu. 720p à 30 images par seconde convient au Wi-Fi d'une église ; 1080p demande un Wi-Fi 5 GHz ou le câble.

## Pour le téléphone (protocole)

Nouvelles commandes : `noir`, `default.set`, `agenda.get`, `agenda.show`, `agenda.add`, `cam.info`, `cam.ack`, et les images MyCam en messages binaires. L'état envoyé au téléphone donne aussi le vrai mode de l'écran, l'écran noir, l'écran par défaut et le déroulé. Les anciennes versions de l'app continuent de marcher.
