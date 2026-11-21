using SmashIt.Core;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.IO;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Same default as the app: lossy PDFs get their over-detailed images scaled down to 150 dpi.
var options = CompressionOptions.Default with { PdfMaxImageDpi = 150 };
var inputs = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    switch (arg)
    {
        case "-h" or "--help" or "/?":
            PrintHelp();
            return 0;
        case "--lossless":
            options = options with { Mode = CompressionMode.Lossless };
            break;
        case "--lossy":
            options = options with { Mode = CompressionMode.Lossy };
            break;
        case "-q" or "--quality" when i + 1 < args.Length && int.TryParse(args[i + 1], out var quality):
            options = options with { Mode = CompressionMode.Lossy, Quality = Math.Clamp(quality, 1, 100) };
            i++;
            break;
        case "-t" or "--to" when i + 1 < args.Length:
            if (!MediaFormatInfo.TryParse(args[++i], out var target))
                return Fail(L10n.T($"Unknown format: {args[i]}", $"Format inconnu : {args[i]}"));
            options = options with { TargetFormat = target };
            break;
        case "--folder":
            options = options with
            {
                OriginalHandling = OriginalHandling.KeepAlongside, KeptOriginalFolder = CompressionOptions.CompressedFolderName,
            };
            break;
        case "--copy-all":
            options = options with
            {
                OriginalHandling = OriginalHandling.KeepAlongside, KeptOriginalFolder = CompressionOptions.CompressedFolderName,
                CopyAlreadyOptimal = true,
            };
            break;
        case "--keep":
            options = options with { OriginalHandling = OriginalHandling.KeepAlongside };
            break;
        case "--pdf-dpi" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dpi) && dpi >= 0:
            options = options with { PdfMaxImageDpi = dpi };
            i++;
            break;
        case "--max-size" when i + 1 < args.Length && int.TryParse(args[i + 1].TrimEnd('x', 'p', 'X', 'P'), out var longSide) && longSide >= 0:
            options = options with { ImageMaxSide = longSide };
            i++;
            break;
        case "--color-detail" when i + 1 < args.Length:
            ColorDetail? detail = args[++i].ToLowerInvariant() switch
            {
                "auto" => ColorDetail.Automatic,
                "full" or "444" or "4:4:4" => ColorDetail.Full,
                "reduced" or "420" or "4:2:0" => ColorDetail.Reduced,
                _ => null,
            };
            if (detail is null) return Fail(L10n.T($"Unknown color detail: {args[i]} (auto, full, reduced)", $"Détail des couleurs inconnu : {args[i]} (auto, full, reduced)"));
            options = options with { Tuning = options.Tuning with { ColorDetail = detail.Value } };
            break;
        case "--jpeg" when i + 1 < args.Length:
            JpegEncoding? encoding = args[++i].ToLowerInvariant() switch
            {
                "auto" => JpegEncoding.Automatic,
                "progressive" => JpegEncoding.Progressive,
                "baseline" => JpegEncoding.Baseline,
                _ => null,
            };
            if (encoding is null) return Fail(L10n.T($"Unknown JPEG encoding: {args[i]} (auto, progressive, baseline)", $"Encodage JPEG inconnu : {args[i]} (auto, progressive, baseline)"));
            options = options with { Tuning = options.Tuning with { JpegEncoding = encoding.Value } };
            break;
        case "--colors" when i + 1 < args.Length && int.TryParse(args[i + 1], out var colors) && colors is 0 or (>= 2 and <= 256):
            options = options with { Tuning = options.Tuning with { MaxColors = colors } };
            i++;
            break;
        case "--no-dither":
            options = options with { Tuning = options.Tuning with { Dithering = false } };
            break;
        case "--zopfli":
            options = options with { Tuning = options.Tuning with { Zopfli = true } };
            break;
        case "--video-max" when i + 1 < args.Length && int.TryParse(args[i + 1].TrimEnd('p', 'P'), out var shortSide) && shortSide >= 0:
            options = options with { VideoMaxShortSide = shortSide };
            i++;
            break;
        case "--codec" when i + 1 < args.Length:
            VideoCodec? codec = args[++i].ToLowerInvariant() switch
            {
                "h264" or "h.264" or "avc" => VideoCodec.H264,
                "hevc" or "h265" or "h.265" => VideoCodec.Hevc,
                "av1" => VideoCodec.Av1,
                _ => null,
            };
            if (codec is null) return Fail(L10n.T($"Unknown codec: {args[i]} (h264, hevc, av1)", $"Codec inconnu : {args[i]} (h264, hevc, av1)"));
            options = options with { VideoCodec = codec.Value };
            break;
        case "--keep-metadata":
            options = options with { StripMetadata = false };
            break;
        case "--strip-metadata":
            options = options with { StripMetadata = true };
            break;
        case "-e" or "--effort" when i + 1 < args.Length:
            CompressionEffort? effort = args[++i].ToLowerInvariant() switch
            {
                "fast" or "rapide" => CompressionEffort.Fast,
                "balanced" or "equilibre" or "équilibré" => CompressionEffort.Balanced,
                "max" or "maximum" => CompressionEffort.Maximum,
                _ => null,
            };
            if (effort is null) return Fail(L10n.T($"Unknown effort: {args[i]} (fast, balanced, max)", $"Effort inconnu : {args[i]} (fast, balanced, max)"));
            options = options with { Effort = effort.Value };
            break;
        case "--no-recycle":
            options = options with { OriginalHandling = OriginalHandling.Overwrite };
            break;
        default:
            if (arg.StartsWith('-')) return Fail(L10n.T($"Unknown option: {arg}", $"Option inconnue : {arg}"));
            inputs.Add(arg);
            break;
    }
}

var files = InputCollector.Expand(inputs, CompressionOptions.CompressedFolderName).ToList();
if (files.Count == 0)
{
    PrintHelp();
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var pipeline = SmashItEngine.CreateDefaultPipeline();
var results = new CompressionResult[files.Count];
var printLock = new object();

await Parallel.ForEachAsync(
    files.Select((path, index) => (path, index)),
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cts.Token },
    async (item, ct) =>
    {
        var result = await pipeline.ProcessAsync(item.path, options, ct);
        results[item.index] = result;
        lock (printLock) PrintResult(result);
    });

var saved = results.Sum(r => r?.SavedBytes ?? 0);
var total = results.Sum(r => r?.OriginalSize ?? 0);
Console.WriteLine();
Console.WriteLine(saved >= 0
    ? L10n.T(
        $"Total: {L10n.Size(saved)} saved out of {L10n.Size(total)} ({L10n.Percent(total > 0 ? (double)saved / total : 0)}).",
        $"Total : {L10n.Size(saved)} économisés sur {L10n.Size(total)} ({L10n.Percent(total > 0 ? (double)saved / total : 0)}).")
    : L10n.T(
        $"Total: {L10n.Size(-saved)} more than before ({L10n.Size(total)}).",
        $"Total : {L10n.Size(-saved)} de plus qu'au départ ({L10n.Size(total)})."));
return results.Any(r => r?.Status == CompressionStatus.Failed) ? 2 : 0;

static void PrintResult(CompressionResult r)
{
    var name = Path.GetFileName(r.SourcePath);
    var line = r.Status switch
    {
        CompressionStatus.Optimized when r.OutputPath != r.SourcePath =>
            $"✔ {name} → {Path.GetFileName(r.OutputPath)}  {L10n.Size(r.OriginalSize)} → {L10n.Size(r.FinalSize)}  (-{L10n.Percent(r.SavedRatio)})  [{r.CompressorId}]",
        CompressionStatus.Optimized => $"✔ {name}  {L10n.Size(r.OriginalSize)} → {L10n.Size(r.FinalSize)}  (-{L10n.Percent(r.SavedRatio)})  [{r.CompressorId}]",
        CompressionStatus.Converted => $"✔ {name} → {Path.GetFileName(r.OutputPath)}  {L10n.Size(r.OriginalSize)} → {L10n.Size(r.FinalSize)}  [{r.CompressorId}]",
        CompressionStatus.AlreadyOptimal when r.OutputPath is not null =>
            $"• {name}  {L10n.T("already optimal, copied", "déjà optimal, copié")}",
        CompressionStatus.AlreadyOptimal => $"• {name}  {L10n.T("already optimal", "déjà optimal")}",
        CompressionStatus.Unsupported => $"– {name}  {r.Message}",
        CompressionStatus.Cancelled => $"– {name}  {L10n.T("cancelled", "annulé")}",
        _ => $"✖ {name}  {r.Message}",
    };
    Console.WriteLine(line);
    if (r.IsSuccess && r.Message is { } note) Console.WriteLine($"    {note}");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp() => Console.WriteLine(L10n.T("""
    Smash It — image, PDF and video compression

    Usage: smashit [options] <files or folders…>

      --lossless           Lossless (default)
      --lossy              Lossy
      -q, --quality <n>    Quality 1–100 (turns on lossy mode)
      -t, --to <format>    Converts to jpg, png, webp, avif, heic, tif, jxr, bmp
      -e, --effort <n>     fast, balanced (default) or max
      --codec <codec>      Video: h264 (default), hevc or av1 (smallest, slower); needs --lossy or -q
      --pdf-dpi <n>        Lossy PDFs: scale images down to n dpi at most (default 150, 0 keeps them)
      --video-max <n>      Videos: bring the short side down to n pixels, e.g. 1080 or 720 (default: keep)
      --max-size <n>       Lossy images: bring the long side down to n pixels, e.g. 2560 (default: keep)
      --color-detail <d>   Lossy JPEG and AVIF: auto (default), full (4:4:4) or reduced (4:2:0)
      --jpeg <e>           JPEG encoding: auto (the smaller one, default), progressive or baseline
      --colors <n>         Lossy PNG and GIF: at most n colors, 2–256 (default: up to 256)
      --no-dither          Lossy PNG: no dithering, much smaller for screenshots and flat images
      --zopfli             PNG: Zopfli compression, a few % smaller, dozens of times slower
      --keep               Keeps the original, writes "name-compressed.ext" next to it
      --folder             Keeps the original, writes the result into a "compressed" folder next to it
      --copy-all           Like --folder, and also copies the files that are already optimal
      --no-recycle         Deletes the original instead of sending it to the Recycle Bin
      --keep-metadata      Keeps EXIF, XMP, IPTC and comments
      --strip-metadata     Removes them (default; color profile and orientation are kept)
      -h, --help           Shows this help
    """, """
    Smash It — compression d'images, de PDF et de vidéos

    Usage : smashit [options] <fichiers ou dossiers…>

      --lossless           Sans perte (par défaut)
      --lossy              Avec pertes
      -q, --quality <n>    Qualité 1–100 (active le mode avec pertes)
      -t, --to <format>    Convertit vers jpg, png, webp, avif, heic, tif, jxr, bmp
      -e, --effort <n>     fast, balanced (par défaut) ou max
      --codec <codec>      Vidéo : h264 (par défaut), hevc ou av1 (le plus léger, plus lent) ; nécessite --lossy ou -q
      --pdf-dpi <n>        PDF avec pertes : réduit les images à n ppp au plus (150 par défaut, 0 les garde)
      --video-max <n>      Vidéos : ramène le petit côté à n pixels, ex. 1080 ou 720 (par défaut : inchangé)
      --max-size <n>       Images avec pertes : ramène le grand côté à n pixels, ex. 2560 (par défaut : inchangé)
      --color-detail <d>   JPEG et AVIF avec pertes : auto (par défaut), full (4:4:4) ou reduced (4:2:0)
      --jpeg <e>           Encodage JPEG : auto (le plus petit, par défaut), progressive ou baseline
      --colors <n>         PNG et GIF avec pertes : n couleurs au plus, 2–256 (par défaut : jusqu'à 256)
      --no-dither          PNG avec pertes : sans tramage, bien plus petit pour les captures et les aplats
      --zopfli             PNG : compression Zopfli, quelques % plus petit, des dizaines de fois plus lent
      --keep               Garde l'original, écrit « nom-compressed.ext » à côté
      --folder             Garde l'original, écrit le résultat dans un dossier « compressed » à côté
      --copy-all           Comme --folder, et y copie aussi les fichiers déjà optimaux
      --no-recycle         Supprime l'original au lieu de l'envoyer à la Corbeille
      --keep-metadata      Conserve EXIF, XMP, IPTC et commentaires
      --strip-metadata     Les supprime (par défaut ; profil couleur et orientation conservés)
      -h, --help           Affiche cette aide
    """));
