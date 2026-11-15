using SmashIt.Core;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.IO;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var options = CompressionOptions.Default;
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
                return Fail($"Format inconnu : {args[i]}");
            options = options with { TargetFormat = target };
            break;
        case "--keep":
            options = options with { OriginalHandling = OriginalHandling.KeepAlongside };
            break;
        case "--no-recycle":
            options = options with { OriginalHandling = OriginalHandling.Overwrite };
            break;
        default:
            if (arg.StartsWith('-')) return Fail($"Option inconnue : {arg}");
            inputs.Add(arg);
            break;
    }
}

var files = InputCollector.Expand(inputs).ToList();
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
    ? $"Total : {Format.Size(saved)} économisés sur {Format.Size(total)} ({Format.Percent(total > 0 ? (double)saved / total : 0)})."
    : $"Total : {Format.Size(-saved)} de plus qu'au départ ({Format.Size(total)}).");
return results.Any(r => r?.Status == CompressionStatus.Failed) ? 2 : 0;

static void PrintResult(CompressionResult r)
{
    var name = Path.GetFileName(r.SourcePath);
    var line = r.Status switch
    {
        CompressionStatus.Optimized => $"✔ {name}  {Format.Size(r.OriginalSize)} → {Format.Size(r.FinalSize)}  (-{Format.Percent(r.SavedRatio)})",
        CompressionStatus.Converted => $"✔ {name} → {Path.GetFileName(r.OutputPath)}  {Format.Size(r.OriginalSize)} → {Format.Size(r.FinalSize)}",
        CompressionStatus.AlreadyOptimal => $"• {name}  déjà optimal",
        CompressionStatus.Unsupported => $"– {name}  {r.Message}",
        CompressionStatus.Cancelled => $"– {name}  annulé",
        _ => $"✖ {name}  {r.Message}",
    };
    Console.WriteLine(line);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp() => Console.WriteLine("""
    Smash It — compression d'images, de vidéos et de PDF

    Usage : smashit [options] <fichiers ou dossiers…>

      --lossless           Sans perte (par défaut)
      --lossy              Avec pertes
      -q, --quality <n>    Qualité 1–100 (active le mode avec pertes)
      -t, --to <format>    Convertit vers jpg, png, tif, bmp, jxr, heic…
      --keep               Garde l'original, écrit « nom.smashed.ext » à côté
      --no-recycle         Écrase l'original au lieu de l'envoyer à la Corbeille
      -h, --help           Affiche cette aide
    """);

internal static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        < 0 => "-" + Size(-bytes),
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} Ko",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.##} Mo",
        _ => $"{bytes / (1024d * 1024 * 1024):0.##} Go",
    };

    public static string Percent(double ratio) => $"{ratio * 100:0.#} %";
}
