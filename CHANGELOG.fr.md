# Journal des modifications

Les nouveautés de chaque version de Smash It, de la plus récente à la plus ancienne.

<!--
Traduction française de CHANGELOG.md, montrée dans « Voir les nouveautés » quand l'app est en français.
- CHANGELOG.md reste la référence (il sert aussi de texte aux releases GitHub) : traduire ici chaque nouvelle section, avec le même titre "## vX.Y.Z".
- Une version absente de ce fichier s'affiche en anglais : rien ne casse si la traduction a du retard.
-->

## Next version

### Nouveautés
- **Les GIF animés deviennent des vidéos, souvent plus de 10 fois plus légères.** Avec pertes, choisissez MP4 ou WebM sous « GIF animés » dans les réglages Images. Dans nos tests, un GIF tiré d'un clip est passé de 4,85 Mo à 364 Ko, un enregistrement d'écran de 1,23 Mo à 22 Ko, chaque image gardant sa durée. La vidéo ne remplace le GIF que si elle est plus petite ; les GIF avec des zones transparentes et les GIF fixes restent des GIF. Sur une page web, affichez-la avec une balise vidéo au lieu d'une image.
- **Les fichiers son.** MP3, M4A, WAV, AIFF, FLAC, Ogg et Opus, dans les nouveaux réglages Son. Sans perte, un WAV devient un FLAC environ deux fois plus léger sans perdre un échantillon, et les autres fichiers perdent leurs tags et leur pochette si vous supprimez les métadonnées. Avec pertes, choisissez Musique ou Voix : dans nos tests, une minute de musique en qualité CD est passée de 10,09 Mo à 763 Ko en Opus, et un chapitre de livre audio a perdu la moitié de son poids en MP3, les trois quarts en Opus. Gardez le format de chaque fichier, ou passez tout en MP3, AAC ou Opus. Les préréglages Web et E-mail couvrent désormais le son.

### Corrections
- Les vidéos AV1 gardent désormais leur date et leur lieu quand « Supprimer les métadonnées » est décoché ; l'app les retirait toujours.

## v0.5.0 · 21 novembre 2026

### Nouveautés
- **Des vidéos AV1, environ un tiers plus légères.** Choisissez AV1 comme codec vidéo : les vidéos sortent environ 36 % plus petites qu'en H.264 à qualité visible égale, parfois même plus belles. L'encodage prend plusieurs fois plus de temps, et la lecture fonctionne dans les navigateurs actuels, sur les téléphones récents et sous Windows avec l'extension gratuite « AV1 Video Extension ». H.264 reste le choix par défaut, pour les vidéos qui doivent être lues partout. L'encodeur AV1 (FFmpeg) alourdit l'installeur d'environ 45 Mo.
- **Des photos plus légères.** Avec pertes, choisissez une taille maximale (3840, 2560, 1920 ou 1280 pixels sur le grand côté) et les images plus grandes y sont réduites, jamais agrandies. Une grande photo à 2560 px pèse souvent moitié moins, à 1920 px quatre fois moins, et reste nette sur n'importe quel écran. Le préréglage Web utilise 2560 px, E-mail 1920 px.
- **Réglages fins.** Sous Images dans les réglages, pour qui veut plus de contrôle : le détail des couleurs en JPEG et AVIF, le JPEG progressif ou classique, un nombre maximum de couleurs en PNG et GIF, le tramage activé ou non, et Zopfli pour des PNG au plus petit. Sans tramage, une capture d'écran avec pertes peut devenir plusieurs fois plus légère.

### Améliorations
- **La fenêtre s'ouvre là où vous l'avez laissée**, à la même taille, agrandie si elle l'était. Si cet écran n'est plus branché, elle s'ouvre au milieu de l'écran principal.

## v0.4.1 · 20 novembre 2026

Cette version apporte aussi tout le contenu de la v0.4.0 (préréglages, vidéos de téléphone plus légères, métadonnées des photos d'iPhone conservées, zoom à 100 %…), dont le téléchargement n'était pas disponible.

### Améliorations
- **« Voir les nouveautés » dans votre langue.** Quand Smash It est en français, les notes de chaque mise à jour s'affichent désormais en français aussi.

### Corrections
- Les tailles juste sous le mégaoctet s'affichent « 0,98 Mo » au lieu de « 1003,4 Ko ».

## v0.4.0 · 19 novembre 2026

### Nouveautés
- **Préréglages.** Choisissez « Web », « E-mail » ou « Archive » et tous les réglages suivent, pour les images, les PDF et les vidéos. La liste se trouve en bas de la fenêtre, à côté du bouton Réglages. Modifiez un réglage et le préréglage devient « Personnalisé ».
- **Des vidéos de téléphone plus légères.** Choisissez une résolution maximale, 1080p ou 720p, et les vidéos 4K y sont ramenées : souvent 3 à 4 fois plus petites, toujours nettes sur n'importe quel écran. Le préréglage Web utilise 1080p, E-mail 720p.
- **Vos photos gardent leur histoire.** Avec « Supprimer les métadonnées » décoché, la date, l'appareil et le lieu d'un HEIC d'iPhone, d'un AVIF ou d'un WebP suivent désormais la photo quand vous la convertissez.
- **Zoom à 100 % dans la vue avant/après.** Appuyez sur « 100 % » (ou Z) pour voir chaque pixel et juger de près une compression avec pertes. Appuyez à nouveau pour revoir l'image entière.

### Améliorations
- **Mieux avec un lecteur d'écran.** Chaque fichier de la liste est lu en une phrase (« photo.jpg, 2,3 Mo → 1,1 Mo, −52 % »), chaque bouton a un nom, et les réglages qui se ressemblaient ont maintenant des noms qui les distinguent.
- **Entrée ouvre la vue avant/après** du fichier sélectionné, comme un double-clic.
- **Une fenêtre vide plus nette.** « Ajouter des fichiers » se trouve juste sous la zone de dépôt. Les boutons de la liste (Ajouter, Recompresser, Vider la liste) et les titres de colonnes n'apparaissent qu'une fois des fichiers ajoutés.
- **La touche Tab suit l'écran** : les fichiers, puis les réglages quand ils sont ouverts, puis la barre du bas.
- **Texte plus grand, rien de coupé.** Si vous avez agrandi le texte dans Windows, les colonnes de la liste et le panneau de réglages s'agrandissent avec lui.

### Corrections
- Les WebP produits par Smash It gardent désormais la date, l'appareil et le lieu quand vous choisissez de garder les métadonnées. Jusqu'ici, seul le profil de couleurs était conservé.

## v0.3.0 · 19 novembre 2026

### Nouveautés
- **Des GIF plus légers, animations comprises.** Smash It compresse désormais les GIF en gardant chaque image et le bouclage. Avec pertes, ils deviennent souvent bien plus légers.
- **Des SVG plus propres et plus légers.** Smash It retire ce que les outils de design laissent dans les fichiers SVG et les écrit de façon plus compacte. Ils s'affichent exactement pareil.
- **Des PDF scannés bien plus légers.** Avec pertes, les images bien plus détaillées que leur taille sur la page sont ramenées à 150 ppp (écran) ou 300 ppp (impression), au choix. Un document scanné devient souvent 5 à 10 fois plus petit, et se lit tout aussi bien.

### Améliorations
- Les fichiers que Smash It ne peut pas traiter, comme un PDF signé, indiquent maintenant « Non pris en charge » avec la raison, au lieu d'« Erreur ».

### Corrections
- Dans « Voir les nouveautés », les éléments longs ne sont plus coupés sur plusieurs lignes.

## v0.2.0 · 19 novembre 2026

### Nouveautés
- **Clic droit pour compresser.** Sélectionnez des images, des PDF, des vidéos ou des dossiers entiers dans l'Explorateur et choisissez « Compresser avec Smash It ». Sous Windows 11, l'entrée se trouve dans « Afficher plus d'options ». Activable ou non dans les réglages.
- **Des originaux bien rangés.** Quand vous compressez plusieurs fichiers, choisissez où vont les copies allégées : à côté des originaux, ou ensemble dans un dossier « compressed ». Smash It retient votre choix.
- **Le lot complet au même endroit.** Avec le dossier « compressed », les fichiers déjà aussi légers que possible peuvent y être copiés aussi.
- **Des PNG bien plus petits** avec pertes.
- **Mises à jour en un clic.** Smash It vous prévient quand une nouvelle version sort, montre ses nouveautés et se met à jour tout seul.
- **Vos réglages sont conservés** d'un lancement à l'autre.

### Améliorations
- La barre du bas indique combien de fichiers sont en cours de compression et combien attendent.
- Un bouton **d'arrêt** interrompt un lot. Les fichiers pas encore compressés restent tels quels.
- Les réglages ont maintenant une section « À propos », avec les projets open source sur lesquels repose Smash It.

## v0.1.0 · 19 novembre 2026

La première version publique.

- Allégez images (JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF), PDF et vidéos, sans perte ou avec pertes.
- Convertissez des images dans un autre format.
- Comparez avant et après pour chaque image.
- Vos originaux sont conservés, sauf si vous choisissez autrement.
- Disponible en anglais et en français.
