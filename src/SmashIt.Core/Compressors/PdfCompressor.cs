using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Pdf;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Shrinks a PDF by recompressing its images, without rebuilding the document.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Lossy mode only: qpdf re-encodes images as JPEG at the chosen quality, keeping each one only if smaller.</item>
/// <item>qpdf exports the document as JSON with every stream in its own file.</item>
/// <item>jpegtran optimizes each JPEG image losslessly (progressive, optimal Huffman tables).</item>
/// <item>qpdf writes the new image data back under the same object numbers, packs objects into object streams
/// and recompresses Flate streams at level 9. Text, fonts, links, forms and bookmarks are untouched.</item>
/// <item>The result is checked by qpdf and must have the same number of pages.</item>
/// </list>
/// Signed PDFs (any change breaks the signature) and encrypted PDFs are refused.
/// </remarks>
public sealed class PdfCompressor : ICompressor
{
    private readonly Qpdf? _qpdf = Qpdf.Find();
    private readonly string? _jpegtran = ExternalTool.Find("jpegtran.exe");

    public string Id => "qpdf";

    public string DisplayName => "qpdf";

    public bool IsAvailable => _qpdf is not null;

    public bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        input == MediaFormat.Pdf && output == MediaFormat.Pdf;

    public async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var qpdf = _qpdf ?? throw new ExternalToolException(L10n.T("qpdf not found.", "qpdf est introuvable."));
        var work = job.OutputPath + ".work";
        Directory.CreateDirectory(work);
        try
        {
            if (await qpdf.IsEncryptedAsync(job.InputPath, cancellationToken))
                throw new NotSupportedException(L10n.T("Encrypted PDF: not supported.", "PDF chiffré : non pris en charge."));

            var pages = await qpdf.CountPagesAsync(job.InputPath, cancellationToken);
            var source = job.InputPath;

            // 1. Lossy: qpdf re-encodes images (Flate → JPEG, and existing JPEGs at the new quality).
            if (job.Options.Mode == CompressionMode.Lossy)
            {
                var lossy = Path.Combine(work, "lossy.pdf");
                await qpdf.RunAsync(
                [
                    source, "--optimize-images", $"--jpeg-quality={Math.Clamp(job.Options.Quality, 1, 100)}",
                    "--warning-exit-0", lossy,
                ], cancellationToken);
                source = lossy;
            }

            // 2. Export streams.
            var json = Path.Combine(work, "document.json");
            await qpdf.RunAsync(
            [
                source, "--json-output", "--json-key=qpdf", "--json-stream-data=file",
                $"--json-stream-prefix={Path.Combine(work, "stream")}", "--warning-exit-0", json,
            ], cancellationToken);
            var document = QpdfJson.Load(json);
            if (document.IsSigned())
                throw new NotSupportedException(L10n.T(
                    "Signed PDF: any change would invalidate the signature.",
                    "PDF signé : toute modification invaliderait la signature."));

            // 3. Lossy: images far finer than needed at their size on the page are scaled down.
            //    Then every JPEG is optimized losslessly.
            var maxDpi = job.Options.Mode == CompressionMode.Lossy ? job.Options.PdfMaxImageDpi : 0;
            var displaySizes = maxDpi > 0 ? document.ImageDisplaySizes() : [];
            var replacements = new List<(PdfImageStream, string)>();
            foreach (var original in document.Images().Where(i => i.IsPlainJpeg))
            {
                var image = original;
                var data = image.DataFile;
                if (displaySizes.TryGetValue(image.ObjectKey, out var shown) &&
                    image.CanResample(document.Resolve, out var grayscale) &&
                    ResampledSize(image.Width, image.Height, shown.Width, shown.Height, maxDpi) is { } size)
                {
                    var (width, height) = size;
                    var resampled = data + ".resampled.jpg";
                    await JpegResampler.ResizeAsync(data, resampled, (uint)width, (uint)height, grayscale,
                        Math.Clamp(job.Options.Quality, 1, 100), cancellationToken);
                    if (new FileInfo(resampled).Length < new FileInfo(data).Length)
                    {
                        var dictionary = image.Dictionary.DeepClone().AsObject();
                        dictionary["/Width"] = width;
                        dictionary["/Height"] = height;
                        image = image with { Dictionary = dictionary };
                        data = resampled;
                    }
                }

                var optimized = _jpegtran is null ? null : await TryOptimizeJpegAsync(data, job.Options, cancellationToken);
                if (optimized is not null) data = optimized;
                if (data != original.DataFile) replacements.Add((image, data));
            }

            // 4. Write back.
            var arguments = new List<string> { source };
            if (replacements.Count > 0)
            {
                var update = Path.Combine(work, "update.json");
                QpdfJson.WriteUpdate(update, replacements);
                arguments.Add($"--update-from-json={update}");
            }
            arguments.AddRange(
            [
                "--object-streams=generate", "--compress-streams=y", "--recompress-flate", "--compression-level=9",
                "--warning-exit-0", job.OutputPath,
            ]);
            await qpdf.RunAsync(arguments, cancellationToken);

            // 5. Safety checks.
            await qpdf.CheckAsync(job.OutputPath, cancellationToken);
            if (await qpdf.CountPagesAsync(job.OutputPath, cancellationToken) != pages)
                throw new InvalidDataException(L10n.T(
                    "The new PDF doesn't have the same number of pages: result discarded.",
                    "Le PDF produit n'a pas le même nombre de pages : résultat ignoré."));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Ghostscript's rule: an image is only scaled down when it is more than 1.5 times finer than the target, so
    /// one just above it isn't resampled for a small gain.
    /// </summary>
    public const double ResampleThreshold = 1.5;

    /// <summary>
    /// The new pixel size of an image of <paramref name="width"/>×<paramref name="height"/> pixels drawn at
    /// <paramref name="shownWidth"/>×<paramref name="shownHeight"/> points (1/72 inch), so that neither direction
    /// goes below <paramref name="maxDpi"/>; <c>null</c> when it should stay as it is.
    /// </summary>
    public static (int Width, int Height)? ResampledSize(int width, int height, double shownWidth, double shownHeight, int maxDpi)
    {
        if (maxDpi <= 0 || shownWidth <= 0 || shownHeight <= 0 || width <= 0 || height <= 0) return null;
        var dpi = Math.Min(width / (shownWidth / 72), height / (shownHeight / 72));
        if (dpi <= maxDpi * ResampleThreshold) return null;
        var scale = maxDpi / dpi;
        return ((int)Math.Max(1, Math.Round(width * scale)), (int)Math.Max(1, Math.Round(height * scale)));
    }

    /// <summary>Returns the path of a smaller, valid JPEG, or <c>null</c> to keep the original stream.</summary>
    private async Task<string?> TryOptimizeJpegAsync(string jpeg, CompressionOptions options, CancellationToken cancellationToken)
    {
        var output = jpeg + ".opt.jpg";
        try
        {
            await ExternalTool.RunAsync(
                _jpegtran!,
                ["-copy", options.StripMetadata ? "none" : "all", "-optimize", "-progressive", "-outfile", output, jpeg],
                cancellationToken);
        }
        catch (ExternalToolException)
        {
            return null; // unusual JPEG: leave it as it is
        }

        var result = new FileInfo(output);
        if (!result.Exists || result.Length == 0 || result.Length >= new FileInfo(jpeg).Length) return null;

        return FormatDetector.DetectFile(output) == MediaFormat.Jpeg ? output : null;
    }
}
