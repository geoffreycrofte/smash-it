namespace SmashIt.Presentation;

/// <summary>
/// The app's texts, for XAML (<c>{x:Bind app:Strings.Add}</c>) and code. Read when a window is built:
/// changing the language rebuilds the main window.
/// </summary>
public static class Strings
{
    private static string T(string english, string french) => L10n.T(english, french);

    // Main window: list
    public static string ColumnFile => T("File", "Fichier");
    public static string ColumnSize => T("Size", "Taille");
    public static string ColumnOptimized => T("Optimized", "Optimisé");
    public static string ColumnGain => T("Saved", "Gain");
    public static string Compare => T("Compare before / after", "Comparer avant / après");
    public static string EmptyTitle => T("Drop your images, PDFs, presentations, videos and sounds here", "Déposez vos images, PDF, présentations, vidéos et sons ici");
    public static string EmptyDetails => T(
        "Files or folders. JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF, SVG, PDF, PowerPoint, Keynote, MP4, MOV, MKV, AVI, MP3, M4A, WAV, FLAC, Ogg, Opus.",
        "Fichiers ou dossiers. JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF, SVG, PDF, PowerPoint, Keynote, MP4, MOV, MKV, AVI, MP3, M4A, WAV, FLAC, Ogg, Opus.");
    public static string DropCaption => T("Compress", "Compresser");

    // Main window: footer
    public static string Settings => T("Settings", "Réglages");
    public static string Add => T("Add", "Ajouter");
    public static string AddFiles => T("Add files", "Ajouter des fichiers");
    public static string Recompress => T("Recompress", "Recompresser");
    public static string RecompressTip => T("Compress again with the current settings", "Recompresser avec les réglages actuels");
    public static string Clear => T("Clear the list", "Vider la liste");
    public static string Stop => T("Stop compressing (the remaining files stay as they are)", "Arrêter la compression (les fichiers restants ne sont pas modifiés)");
    public static string CreditPrefix => T("Free app made by ", "Application gratuite proposée par ");

    // Settings: images
    public static string Images => T("Images", "Images");
    public static string Compression => T("Compression", "Compression");
    public static string Lossless => T("Lossless", "Sans perte");
    public static string Lossy => T("Lossy", "Avec pertes");
    public static string Quality => T("Quality", "Qualité");
    public static string OutputFormat => T("Output format", "Format de sortie");
    public static string ImageSize => T("Maximum size", "Taille maximale");
    public static string ImageSizeName => T("Maximum image size", "Taille maximale des images");
    public static string ImageSizeTip => T(
        "Lossy only: larger images are scaled down to this length on their long side, never enlarged.",
        "Avec pertes uniquement : les images plus grandes sont réduites à cette longueur sur leur grand côté, jamais agrandies.");
    public static string AnimatedGifs => T("Animated GIFs", "GIF animés");
    public static string AnimatedGifsTip => T(
        "Lossy only: an animated GIF becomes a video, often more than 10 times lighter, when it gets smaller. A web page then shows it with a <video> tag instead of <img>. GIFs with transparent areas and still GIFs stay GIFs.",
        "Avec pertes uniquement : un GIF animé devient une vidéo, souvent plus de 10 fois plus légère, s'il y gagne. Une page web l'affiche alors avec une balise <video> au lieu de <img>. Les GIF avec des zones transparentes et les GIF fixes restent des GIF.");
    public static string Effort => T("Effort", "Effort");
    public static string EffortTip => T("More effort: smaller files, slower compression.", "Plus d'effort : fichiers plus petits, compression plus lente.");
    public static string StripMetadata => T("Remove metadata", "Supprimer les métadonnées");
    public static string StripMetadataTip => T(
        "Removes EXIF (camera, date, GPS…), XMP, IPTC and comments. The color profile and orientation are always kept.",
        "Retire EXIF (appareil, date, GPS…), XMP, IPTC et commentaires. Le profil couleur et l'orientation sont toujours conservés.");

    // Settings: images, fine tuning
    public static string FineTuning => T("Fine tuning", "Réglages fins");
    public static string FineTuningNote => T(
        "Per format, for those who want them. Presets don't change these.",
        "Par format, pour qui en a l'usage. Les préréglages n'y touchent pas.");
    public static string ColorDetail => T("Color detail (JPEG, AVIF, lossy)", "Détail des couleurs (JPEG, AVIF, avec pertes)");
    public static string ColorDetailName => T("Color detail", "Détail des couleurs");
    public static string ColorDetailTip => T(
        "Reduced keeps color at half resolution: invisible on photos, smaller files. Full keeps colored text and thin lines sharp.",
        "Réduit garde la couleur à mi-résolution : invisible sur les photos, fichiers plus petits. Complet garde nets le texte coloré et les traits fins.");
    public static string JpegEncoding => T("JPEG encoding", "Encodage JPEG");
    public static string JpegEncodingTip => T(
        "Progressive shows a blurry preview first while a web page loads. Baseline is for old devices and software.",
        "Progressif affiche d'abord un aperçu flou pendant le chargement d'une page web. Classique sert aux appareils et logiciels anciens.");
    public static string MaxColors => T("Maximum colors (PNG, GIF, lossy)", "Couleurs maximum (PNG, GIF, avec pertes)");
    public static string MaxColorsName => T("Maximum colors", "Couleurs maximum");
    public static string MaxColorsTip => T(
        "Fewer colors, smaller files: fine for icons, logos and simple illustrations, visible on photos.",
        "Moins de couleurs, fichiers plus petits : bien pour les icônes, logos et illustrations simples, visible sur les photos.");
    public static string Dithering => T("Dithering (lossy PNG)", "Tramage (PNG avec pertes)");
    public static string DitheringTip => T(
        "Smooths gradients with fine noise. Turn it off for screenshots and flat illustrations: they can get several times smaller.",
        "Adoucit les dégradés avec un bruit fin. Désactivez-le pour les captures d'écran et illustrations en aplats : elles peuvent devenir plusieurs fois plus légères.");
    public static string Zopfli => T("Zopfli (PNG)", "Zopfli (PNG)");
    public static string ZopfliTip => T(
        "A stronger compressor: PNGs about 5 to 10 % smaller, but compressing takes dozens of times longer.",
        "Un compresseur plus poussé : PNG environ 5 à 10 % plus petits, mais une compression des dizaines de fois plus longue.");

    // Names read by screen readers where the visible label alone is ambiguous.
    public static string ImageQualityName => T("Image quality", "Qualité des images");
    public static string PdfQualityName => T("Document image quality", "Qualité des images des documents");
    public static string VideoQualityName => T("Video quality", "Qualité des vidéos");
    public static string FileList => T("Files", "Fichiers");
    public static string BeforeImageName => T("Original image", "Image d'origine");
    public static string AfterImageName => T("Compressed image", "Image compressée");

    // Settings: preset
    public static string Preset => T("Preset", "Préréglage");

    // Settings: PDF
    public static string Pdf => T("Documents (PDF, PowerPoint, Keynote)", "Documents (PDF, PowerPoint, Keynote)");
    public static string PdfImages => T("Document images", "Images du document");
    public static string PdfQuality => T("Image quality", "Qualité des images");
    public static string PdfResolution => T("Maximum image resolution", "Résolution maximale des images");
    public static string PdfNote => T(
        "Text, fonts, links, forms and slides are never changed: only the pictures inside (and the videos and sounds of a PowerPoint) are compressed. Lossy, pictures much finer than their size on the page or slide are scaled down: 150 dpi is plenty for a screen, 300 dpi for print. Keynote pictures keep their size. Signed or encrypted PDFs are skipped.",
        "Texte, polices, liens, formulaires et diapositives ne sont jamais modifiés : seules les images qu'ils contiennent (et les vidéos et sons d'un PowerPoint) sont compressées. Avec pertes, les images bien plus fines que leur taille sur la page ou la diapositive sont réduites : 150 ppp suffisent à l'écran, 300 ppp à l'impression. Les images Keynote gardent leur taille. Les PDF signés ou chiffrés sont ignorés.");

    // Settings: sound
    public static string Audio => T("Sound", "Son");
    public static string AudioCompressionName => T("Sound compression", "Compression du son");
    public static string AudioQualityName => T("Sound quality", "Qualité du son");
    public static string AudioContent => T("Content", "Contenu");
    public static string AudioContentTip => T(
        "Voice: speech comes out in mono at about a third of the bitrate music needs, just as clear.",
        "Voix : la parole sort en mono, à environ un tiers du débit qu'il faut à la musique, tout aussi claire.");
    public static string AudioFormatName => T("Sound output format", "Format de sortie des sons");
    public static string AudioNote => T(
        "Lossless, WAV and AIFF become FLAC, about half the size with every sample kept; MP3, M4A, Ogg and Opus only lose their tags and cover art when metadata is removed (setting under Images). Lossy, music becomes about 96 kbit/s in Opus, 160 in AAC, LAME V2 in MP3. A file already that light in its format is never re-encoded.",
        "Sans perte, les WAV et AIFF deviennent des FLAC, environ deux fois plus légers, sans perdre un échantillon ; les MP3, M4A, Ogg et Opus ne perdent que leurs tags et leur pochette quand les métadonnées sont supprimées (réglage dans Images). Avec pertes, la musique passe à environ 96 kbit/s en Opus, 160 en AAC, LAME V2 en MP3. Un fichier déjà aussi léger dans son format n'est jamais réencodé.");

    // Settings: video
    public static string Video => T("Video", "Vidéo");
    public static string Codec => T("Codec", "Codec");
    public static string VideoResolution => T("Maximum resolution", "Résolution maximale");
    public static string VideoResolutionName => T("Maximum video resolution", "Résolution maximale des vidéos");
    public static string VideoEncoder => T("Encoder (H.264, HEVC)", "Encodeur (H.264, HEVC)");
    public static string VideoEncoderName => T("Video encoder for H.264 and HEVC", "Encodeur vidéo pour H.264 et HEVC");
    public static string VideoEncoderTip => T(
        "ffmpeg keeps the same visible quality whatever the video, and its dates and location if you keep metadata; about real time for 1080p. The Windows encoder is 4 to 6 times faster on the graphics card, but detailed videos (animation, screen recordings) can come out blurry. AV1 always uses ffmpeg.",
        "ffmpeg garde la même qualité visible quelle que soit la vidéo, et sa date et son lieu si vous gardez les métadonnées ; environ le temps réel en 1080p. L'encodeur de Windows est 4 à 6 fois plus rapide sur la carte graphique, mais les vidéos détaillées (animation, enregistrements d'écran) peuvent ressortir floues. L'AV1 passe toujours par ffmpeg.");
    public static string VideoNote => T(
        "Re-encoded to MP4. AV1 is about a third lighter than H.264 at the same quality, a few times slower, and plays in current browsers, recent phones, and Windows with the AV1 extension. MOV, MKV and AVI become MP4, only when they get smaller. Larger videos (4K) are brought down to the chosen resolution, never enlarged.",
        "Réencodage en MP4. L'AV1 est environ un tiers plus léger que le H.264 à qualité égale, plusieurs fois plus lent, et se lit dans les navigateurs actuels, sur les téléphones récents et sous Windows avec l'extension AV1. Les MOV, MKV et AVI deviennent des MP4, seulement s'ils y gagnent. Les vidéos plus grandes (4K) sont ramenées à la résolution choisie, jamais agrandies.");

    // Settings: all files
    public static string AllFiles => T("All files", "Tous les fichiers");
    public static string KeepOriginals => T("Keep the originals", "Garder les originaux");
    public static string KeepOriginalsNote => T(
        "Unchecked, the result replaces the original, which goes to the Recycle Bin.",
        "Décoché, le résultat remplace l'original, qui part dans la Corbeille.");
    public static string SaveResults => T("Save the compressed files", "Enregistrer les fichiers compressés");
    public static string NextToOriginals => T("Next to the originals, as “name-compressed.ext”", "À côté des originaux, sous le nom « nom-compressed.ext »");
    public static string InCompressedFolder => T("In a “compressed” folder next to the originals", "Dans un dossier « compressed » à côté des originaux");
    public static string CopyAlreadyOptimal => T(
        "Also copy the files that are already optimal, so the folder holds the whole set",
        "Y copier aussi les fichiers déjà optimaux, pour avoir le lot complet dans ce dossier");
    public static string AskOutputLocation => T("Ask when I add several files", "Demander quand j'ajoute plusieurs fichiers");

    // Dialog: where do the compressed files go?
    public static string WhereTitle => T("Where should the compressed files go?", "Où enregistrer les fichiers compressés ?");
    public static string WhereIntro(int count) => T(
        $"{count} files to compress. The originals are kept.",
        $"{count} fichiers à compresser. Les originaux sont conservés.");
    public static string DontAskAgain => T("Don't ask again (you can change it in Settings)", "Ne plus demander (modifiable dans les réglages)");
    public static string CompressButton => T("Compress", "Compresser");
    public static string Cancel => T("Cancel", "Annuler");

    // Settings: app
    public static string Language => T("Language", "Langue");
    public static string Explorer => T("Windows Explorer", "Explorateur Windows");
    public static string ContextMenuToggle => T("Right-click menu entry", "Entrée dans le menu clic droit");
    public static string ContextMenuLabel => T("Compress with Smash It", "Compresser avec Smash It");
    public static string ContextMenuNote => T(
        "“Compress with Smash It” on images, PDFs, presentations, videos, sounds and folders, with the settings above. On Windows 11, it's under “Show more options”.",
        "« Compresser avec Smash It » sur les images, PDF, présentations, vidéos, sons et dossiers, avec les réglages ci-dessus. Sous Windows 11, il se trouve dans « Afficher plus d'options ».");

    // Settings: about
    public static string About => T("About", "À propos");
    public static string AppNameAndVersion
    {
        get
        {
            var version = typeof(Strings).Assembly.GetName().Version;
            return version is null ? "Smash It" : $"Smash It {version.Major}.{version.Minor}.{version.Build}";
        }
    }
    public static string CheckForUpdates => T("Check for updates at startup", "Rechercher les mises à jour au démarrage");
    public static string CheckNow => T("Check now", "Rechercher maintenant");
    public static string UpdatesNote => T(
        "Only the list of versions is requested from GitHub; nothing else is sent.",
        "Seule la liste des versions est demandée à GitHub ; rien d'autre n'est envoyé.");
    public static string WhatsNew => T("What's new", "Voir les nouveautés");
    public static string SkipVersion => T("Skip this version", "Ignorer cette version");
    public static string Close => T("Close", "Fermer");
    public static string FreeSoftware => T("Free software under the GPL-3.0 license. ", "Logiciel libre sous licence GPL-3.0. ");
    public static string SourceCode => T("Source code", "Code source");
    public static string OpenSourceProjects => T("Built with these open source projects", "Conçue avec ces projets open source");

    // Preview window
    public static string PreviewTitle => T("Before / after", "Avant / après");
    public static string Before => T("Before", "Avant");
    public static string After => T("After", "Après");
    public static string PreviewHint => T(
        "Drag the handle or use ← →. Double-click: back to the middle. Z: 100 %. Esc: close.",
        "Glissez la poignée ou utilisez ← →. Double-clic : retour au centre. Z : 100 %. Échap : fermer.");
    public static string ActualSize => "100 %";
    public static string ActualSizeName => T("100 % zoom", "Zoom à 100 %");
    public static string ActualSizeTip => T(
        "Show at 100 %, one image pixel per screen pixel (Z). Scroll with the wheel, Shift+wheel sideways.",
        "Afficher à 100 %, un pixel de l'image par pixel d'écran (Z). Défilement à la molette, Maj+molette sur le côté.");
    public static string Divider => T("Before / after divider", "Séparateur avant / après");
    public static string DividerTip => T("Before on the left, after on the right", "Avant à gauche, après à droite");
    public static string Loading => T("Loading…", "Chargement…");
    public static string PreviewFailed(string reason) => T($"Can't show the preview: {reason}", $"Aperçu impossible : {reason}");
}
