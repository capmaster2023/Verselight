# MyChurch 4.0.0 : l'audio refait comme OBS 32, nouvelle app mobile et MyCam (le téléphone devient une caméra)

**Pas encore essayé sur du vrai matériel d'église (projecteur, carte son, micros USB, téléphones, Wi-Fi de l'église). Faites un essai complet avant de l'utiliser pendant un culte.**

## L'audio comme OBS 32

### Une entrée branchée apparaît toute seule dans le mélangeur

Branchez un micro USB, une carte son ou une table de mixage : il apparaît dans le mélangeur en 2 secondes, avec son nom (« Nouvelle entrée audio : « Behringer UMC404HD » ajoutée au mélangeur »). Débranché, il devient **inactif** ; rebranché, il revient tout seul, avec son volume, ses filtres et ses destinations. Les entrées déjà branchées lors de la mise à jour ne sont pas ajoutées (comme avant, « + Source audio »). Réglages › Audio › « Ajouter au mélangeur les entrées audio branchées » pour l'arrêter.

### Réglages › Audio, comme la page Audio des paramètres d'OBS

- **Général** : fréquence d'échantillonnage (44,1 ou 48 kHz) et canaux (mono, stéréo, 2.1, 4.0, 4.1, 5.1, 7.1). Comme dans OBS, ils s'appliquent au prochain démarrage : le bouton **Redémarrer MyChurch** le fait tout de suite. Avec plus de deux canaux, les enregistrements et les sorties MyVirtualSound reçoivent un mélange stéréo.
- **Périphériques audio globaux** : Audio du bureau, Audio du bureau 2, Micro/Aux, Micro/Aux 2, 3 et 4, chacun « Désactivé », « Par défaut » (suit le périphérique choisi dans Windows) ou un périphérique. Au premier démarrage, « Audio du bureau » et « Micro/Aux » sont réglés sur « Par défaut », comme dans OBS (seulement si le mélangeur est vide : rien ne change dans un mélangeur déjà rempli).
- **Vumètres** : vitesse de retombée (rapide, moyenne PPM de type I, lente PPM de type II) et type de crête (crête d'échantillon ou crête réelle).
- **Avancé** : périphérique d'écoute (casque de la régie), **désactiver l'atténuation audio de Windows** (MyChurch n'est plus baissé pendant un appel Teams ou Zoom), mise en mémoire tampon audio à faible latence (pour les sorties Decklink et NDI).
- **Raccourcis** : pour chaque source, **appuyer pour couper le son** et **appuyer pour parler**, avec leur délai et leur touche (clavier, ou boutons 4 et 5 de la souris). Les touches marchent même quand MyChurch est en arrière-plan.

### Le mélangeur d'OBS 32.1

- Les périphériques globaux sont toujours affichés et **épinglés à gauche**. Chaque bande porte son étiquette : **GLOBALE**, **ÉPINGLÉE**, **ACTIVE**, **INACTIVE** (périphérique débranché, fichier introuvable) ou **MASQUÉE**.
- Un clic sur le nom d'une bande (ou un clic droit) : **Épingler**, **Masquer**, renommer, entrée de la carte son, périphérique, mono, écoute au casque, filtres, propriétés avancées, ordre.
- Barre du mélangeur : « 2 masquées » (un clic les montre), **Options** (afficher les sources masquées, afficher les sources inactives, les garder à droite, tout réafficher), bouton de **disposition verticale ou horizontale**, et les **propriétés audio avancées**.
- Le casque s'allume ou s'éteint d'un clic (écoute et sortie) ; clic droit pour l'écoute seule.

### Propriétés audio avancées

Toutes les sources dans un tableau, comme dans OBS : état, volume (en dB ou en %), mono, **balance**, **décalage de synchronisation** (−950 à 20 000 ms, pour caler le son sur une caméra en retard), écoute au casque et destinations (enregistrements 1 à 5, sorties MyVirtualSound A à D).


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
