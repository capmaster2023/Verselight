# MyChurch 4.2.0 : l'audio simplifié comme OBS, et un logiciel vide au premier lancement

**À installer sur le PC (MyChurch-Setup-4.2.0.exe). L'application du téléphone ne change pas.**

**Testé automatiquement, pas encore sur la carte son de l'église : faites un essai (micros, enregistrement, Zoom) avant un culte.**

## L'audio, comme dans OBS

Il y avait trop de réglages. La 4.2 garde ce qu'OBS montre, et presque rien d'autre.

### Ajouter une source

**+ Source audio**, comme le « + » des sources d'OBS : **Capture audio (entrée)**, **Capture audio (sortie)**, **Média**, ou le son de la vidéo projetée depuis Display.

Une capture audio s'ouvre sur la fenêtre « Propriétés » d'OBS : **Périphérique** (« Par défaut » ou un périphérique) et **Utiliser l'horodatage du périphérique**, avec **Valeurs par défaut**, **OK** et **Annuler**. Une carte son à plusieurs entrées (Behringer, Focusrite…) propose aussi **Entrée** : toutes ensemble, ou une seule (un micro par source, comme avant).

### Le mélangeur

Chaque bande : son nom, son volume, ses vumètres, le fader, **M** (sourdine), le **casque** (monitoring) et **⋮** : Propriétés, Filtres, Renommer, Propriétés audio avancées, déplacer, Supprimer. La roue dentée **⚙** ouvre les propriétés audio avancées.

### Propriétés audio avancées

Le même tableau que dans OBS : nom, état (**Actif** ou **Inactif**), volume (case **%** pour l'afficher en pourcentage), mono, balance, décalage de synchronisation, monitoring audio et **pistes 1 à 6**, avec « Sources actives uniquement ».

### Pistes : enregistrements et Zoom

Comme dans OBS, chaque source coche ses pistes (une nouvelle source va dans les 6), et chaque enregistrement ou sortie MyVirtualSound choisit **sa piste** (« Piste audio »). Exemple : le pasteur seul sur la piste 2 pour l'enregistrement « Prédication ».

**Vos réglages de la 4.1 sont repris** : chaque mélange déjà fait (les sources choisies pour un enregistrement ou pour Zoom) reçoit sa piste, et les sources sont cochées en conséquence.

### Retiré

- **Réglages › Audio** : fréquence, canaux, périphériques audio globaux, vumètres, mise en mémoire tampon à faible latence, raccourcis (appuyer pour parler, appuyer pour couper le son) et ajout automatique des micros branchés. Il reste le **périphérique de monitoring audio** (casque de la régie). « Audio du bureau » et « Micro/Aux » deviennent des sources ordinaires : rien n'est perdu.
- **Mélangeur** : épingler, masquer, sources masquées ou inactives, disposition horizontale, pastilles 1 à 5 et A à D sous les bandes, étiquettes ACTIVE, GLOBALE…
- **Enregistrements et sorties** : « Couper les autres sources » (une piste avec seulement les bonnes sources fait la même chose).
- Le choix d'un autre ffmpeg.exe (celui de MyChurch est inclus).
- MyChurch n'est plus jamais baissé par Windows pendant un appel Zoom ou Teams (c'était une option).

**MyVirtualSound et VB-CABLE ne changent pas** : Réglages › MyVirtualSound › Installer VB-CABLE.

## Premier lancement : un logiciel vide

Une nouvelle installation démarre sans chant (les recueils ne sont plus livrés avec MyChurch), sans source audio et sans enregistrement. Seules deux Bibles libres de droits sont livrées : **Louis Segond 1910** (français) et **King James** (anglais). Les chants s'ajoutent avec **+ Importer chants ou recueil** ou **+ Nouveau chant**.

**Une mise à jour ne supprime rien** : chants, recueils, Bibles importées, déroulés et réglages restent.
