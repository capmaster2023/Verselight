# MyChurch 1.0.2 bêta

- **Duo : deux contenus en même temps à l'écran.** Deux sources Display, un chant + une caméra, une source + un verset, deux chants… Affichez le premier contenu, cliquez sur **Épingler le contenu en direct** (ou touche **P**) : il part dans un coin et l'écran se libère. Affichez ensuite le second : les deux sont à l'écran.
- Nouvelle carte **DUO** dans la colonne de droite, entre « EN DIRECT » et « HABILLAGE DE L'ÉCRAN » : le contenu épinglé, **Retirer le duo**, la place (haut droite, haut gauche, bas droite, bas gauche, **côte à côte**) et la taille (20 à 50 % de l'écran). La place et la taille sont gardées pour la prochaine fois.
- **Retirer de l'écran** (Échap) ne vide que l'écran principal : le contenu épinglé reste. **Retirer le duo** (ou **P**) l'enlève. L'écran noir (**B**) cache tout.
- Clic droit sur une source Display (bibliothèque ou tuile de la grille) : **Afficher puis épingler**.
- Le contenu épinglé reste vivant : une caméra, une fenêtre ou une image épinglée continue d'être capturée pendant qu'autre chose est à l'écran.
- Le Duo sort partout : fenêtre de projection, aperçu de la régie, caméra virtuelle **MyVirtualCam** et export navigateur local.
- **Installation :** la mise à jour ne bloque plus avec « Erreur lors de l'ouverture du fichier en écriture : …\MyVirtualCam.dll » quand OBS, Zoom ou un autre logiciel utilise la caméra virtuelle. Ce logiciel garde l'ancienne version de la caméra jusqu'à ce qu'il soit fermé puis rouvert.
- Le numéro de version affiché dans MyChurch (en haut, et dans Outils › Mise à jour) est maintenant le bon.

- **Menu d'ajout Display** : un bouton **+ Ajouter une source** remplace la liste des boutons. Le menu propose Nouveau texte, Images, Périphérique de capture vidéo, Capture de fenêtre et MyVirtualCam.
- **Projection autonome** : choisissez un moniteur ou une fenêtre avec **Projection**. Cliquer une vignette affiche immédiatement son contenu sur la sortie choisie, Duo compris.
- **Connexion OBS supprimée** : plus de client WebSocket, de dialogue de connexion, de mot de passe ou de pastille « OBS non connecté ». Les anciens réglages sont conservés pour la reprise des données, mais ne déclenchent aucune connexion. L'export navigateur local reste disponible sans connexion à un autre logiciel.
- **Validation** : tests du cœur et de Duo, tests WPF, captures du coin et du mode côte à côte, pixels réels de MyVirtualCam, export navigateur, menu Display et installation de la caméra virtuelle.
