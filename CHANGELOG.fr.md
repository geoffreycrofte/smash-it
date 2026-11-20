# Journal des modifications

Les nouveautés de chaque version de Smash It, de la plus récente à la plus ancienne.

<!--
Traduction française de CHANGELOG.md, montrée dans « Voir les nouveautés » quand l'app est en français.
- CHANGELOG.md reste la référence (il sert aussi de texte aux releases GitHub) : traduire ici chaque nouvelle section, avec le même titre "## vX.Y.Z".
- Une version absente de ce fichier s'affiche en anglais : rien ne casse si la traduction a du retard.
-->

## Next version

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
