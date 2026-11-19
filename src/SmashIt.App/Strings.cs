namespace SmashIt.App;

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
    public static string EmptyTitle => T("Drop your images, PDFs and videos here", "Déposez vos images, PDF et vidéos ici");
    public static string EmptyDetails => T(
        "Files or folders. JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF, SVG, PDF, MP4, MOV, MKV, AVI.",
        "Fichiers ou dossiers. JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF, SVG, PDF, MP4, MOV, MKV, AVI.");
    public static string DropCaption => T("Compress", "Compresser");

    // Main window: footer
    public static string Settings => T("Settings", "Réglages");
    public static string Add => T("Add", "Ajouter");
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
    public static string Effort => T("Effort", "Effort");
    public static string EffortTip => T("More effort: smaller files, slower compression.", "Plus d'effort : fichiers plus petits, compression plus lente.");
    public static string StripMetadata => T("Remove metadata", "Supprimer les métadonnées");
    public static string StripMetadataTip => T(
        "Removes EXIF (camera, date, GPS…), XMP, IPTC and comments. The color profile and orientation are always kept.",
        "Retire EXIF (appareil, date, GPS…), XMP, IPTC et commentaires. Le profil couleur et l'orientation sont toujours conservés.");

    // Settings: PDF
    public static string Pdf => "PDF";
    public static string PdfImages => T("Document images", "Images du document");
    public static string PdfQuality => T("Image quality", "Qualité des images");
    public static string PdfResolution => T("Maximum image resolution", "Résolution maximale des images");
    public static string PdfNote => T(
        "Text, fonts, links and forms are never changed. Lossy, images much finer than their size on the page are scaled down: 150 dpi is plenty for a screen, 300 dpi for print. Signed or encrypted PDFs are skipped.",
        "Texte, polices, liens et formulaires ne sont jamais modifiés. Avec pertes, les images bien plus fines que leur taille sur la page sont réduites : 150 ppp suffisent à l'écran, 300 ppp à l'impression. Les PDF signés ou chiffrés sont ignorés.");

    // Settings: video
    public static string Video => T("Video", "Vidéo");
    public static string Codec => T("Codec", "Codec");
    public static string VideoNote => T(
        "Re-encoded to MP4 with the Windows encoder. MOV, MKV and AVI become MP4, only when they get smaller.",
        "Réencodage en MP4 avec l'encodeur de Windows. Les MOV, MKV et AVI deviennent des MP4, seulement s'ils y gagnent.");

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
        "“Compress with Smash It” on images, PDFs, videos and folders, with the settings above. On Windows 11, it's under “Show more options”.",
        "« Compresser avec Smash It » sur les images, PDF, vidéos et dossiers, avec les réglages ci-dessus. Sous Windows 11, il se trouve dans « Afficher plus d'options ».");

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
        "Drag the handle or use ← →. Double-click: back to the middle. Esc: close.",
        "Glissez la poignée ou utilisez ← →. Double-clic : retour au centre. Échap : fermer.");
    public static string Divider => T("Before / after divider", "Séparateur avant / après");
    public static string DividerTip => T("Before on the left, after on the right", "Avant à gauche, après à droite");
    public static string Loading => T("Loading…", "Chargement…");
    public static string PreviewFailed(string reason) => T($"Can't show the preview: {reason}", $"Aperçu impossible : {reason}");
}
