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
        "Files or folders. JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF, PDF, MP4, MOV, MKV, AVI.",
        "Fichiers ou dossiers. JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF, PDF, MP4, MOV, MKV, AVI.");
    public static string DropCaption => T("Compress", "Compresser");

    // Main window: footer
    public static string Settings => T("Settings", "Réglages");
    public static string Add => T("Add", "Ajouter");
    public static string Recompress => T("Recompress", "Recompresser");
    public static string RecompressTip => T("Compress again with the current settings", "Recompresser avec les réglages actuels");
    public static string Clear => T("Clear the list", "Vider la liste");
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
    public static string PdfNote => T(
        "Text, fonts, links and forms are never changed. Signed or encrypted PDFs are skipped.",
        "Texte, polices, liens et formulaires ne sont jamais modifiés. Les PDF signés ou chiffrés sont ignorés.");

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
        "Checked: the result is saved next to it as “name-compressed.ext”. Unchecked: it replaces the original, which goes to the Recycle Bin.",
        "Coché : le résultat est enregistré à côté sous le nom « nom-compressed.ext ». Décoché : il remplace l'original, qui part dans la Corbeille.");

    // Settings: app
    public static string Language => T("Language", "Langue");

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
