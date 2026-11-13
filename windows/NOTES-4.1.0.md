# MyChurch 4.1.0 : MyCam fluide à 30 i/s (vidéo H.264, comme DroidCam)

**À installer sur le PC (MyChurch-Setup-4.1.0.exe) et sur le téléphone (MyChurch.apk).**

Avant, le téléphone envoyait une photo JPEG pour chaque image (60 à 150 Ko) : sur le Wi-Fi d'une église, ça plafonnait autour de 10 images par seconde, avec du retard. Et dans la pénombre, la caméra du téléphone ralentissait d'elle-même pour mieux exposer.

## Sur le téléphone

- **Vidéo H.264** : l'encodeur vidéo du téléphone envoie de la vraie vidéo, comme DroidCam. Environ 10 fois plus légère que le JPEG (≈ 4 Mb/s en 720p), donc 30 i/s sur le Wi-Fi, avec très peu de retard.
- **30 i/s tenus, même dans la pénombre** : la caméra garde la cadence choisie au lieu de ralentir.
- **Wi-Fi en mode rapide pendant la diffusion** : le téléphone ne met plus son Wi-Fi en veille entre deux images (saccades en moins).
- Si le Wi-Fi faiblit, le débit baisse un moment puis remonte, sans prendre de retard.
- Nouveau réglage **Format vidéo** (MyCam › Réglages) : H.264 (par défaut) ou JPEG. Le format envoyé s'affiche sur l'image (« 1280 × 720 · 30 i/s · H.264 »).

## Sur le PC

- La vidéo H.264 est décodée par le FFmpeg d'OBS déjà livré avec MyChurch (rien à installer). Couleurs identiques au JPEG.
- L'image de la page OBS s'écrit à côté, sans ralentir la caméra.
- La tuile et l'écran montrent le format reçu (« MyCam · 1280 × 720 · 30 i/s · H.264 · Wi-Fi »).

## Compatibilité

- Téléphone 4.1 avec un PC 4.0 : JPEG automatiquement.
- Téléphone 4.0 avec un PC 4.1 : JPEG, comme avant.
- Pour le H.264 et les 30 i/s, mettez les deux à jour.
