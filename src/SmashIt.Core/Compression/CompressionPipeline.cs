using SmashIt.Core.Formats;
using SmashIt.Core.IO;
using SmashIt.Core.Metadata;

namespace SmashIt.Core.Compression;

/// <summary>
/// Compresses one file:
/// <list type="number">
/// <item>every engine able to handle the job runs, the smallest output wins;</item>
/// <item>lossless optimizers (jpegtran, oxipng) then polish the winner if it came from another engine;</item>
/// <item>the metadata the user wants to keep is written into the result;</item>
/// <item>the result replaces the original only when it is smaller (or when a conversion was asked for).</item>
/// </list>
/// </summary>
public sealed class CompressionPipeline(IEnumerable<ICompressor> compressors)
{
    private readonly IReadOnlyList<ICompressor> _compressors = compressors.ToList();

    public IReadOnlyList<ICompressor> Compressors => _compressors;

    public async Task<CompressionResult> ProcessAsync(
        string path,
        CompressionOptions options,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        var originalSize = new FileInfo(path).Length;
        var inputFormat = FormatDetector.DetectFile(path);
        if (inputFormat == MediaFormat.Unknown)
            return new(path, CompressionStatus.Unsupported, originalSize, originalSize, Message: L10n.T("Unrecognized format.", "Format non reconnu."));

        var outputFormat = MediaFormatInfo.ResolveOutput(inputFormat, options.TargetFormat);
        // A conversion the user asked for is kept even if larger; one forced on us (MKV → MP4) is not.
        var requested = outputFormat != inputFormat && options.TargetFormat == outputFormat;

        if (await GifVideoFormatAsync(path, inputFormat, options, cancellationToken).ConfigureAwait(false) is { } videoFormat)
        {
            // Animated GIF → video, kept only when smaller. Nothing is copied to the output folder yet: the GIF may
            // still be compressed below.
            var video = await ProcessAsAsync(path, inputFormat, videoFormat, requested: false,
                options with { CopyAlreadyOptimal = false }, originalSize, cancellationToken, progress).ConfigureAwait(false);
            if (video.Status is CompressionStatus.Converted or CompressionStatus.Cancelled) return video;

            // Not smaller, transparent, or the encoder failed: the GIF stays a GIF, compressed as usual. Why it
            // wasn't converted is said, unless the video was simply larger.
            var gif = await ProcessAsAsync(path, inputFormat, outputFormat, requested, options, originalSize, cancellationToken, progress)
                .ConfigureAwait(false);
            return video.Status is CompressionStatus.AlreadyOptimal || video.Message is null
                ? gif
                : gif with { Message = gif.Message is null ? video.Message : gif.Message + Environment.NewLine + video.Message };
        }

        return await ProcessAsAsync(path, inputFormat, outputFormat, requested, options, originalSize, cancellationToken, progress)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The video format an animated GIF becomes (<see cref="CompressionOptions.AnimatedGifs"/>), or <c>null</c>: not
    /// a GIF, lossless mode, a still GIF, or no engine for it.
    /// </summary>
    private async Task<MediaFormat?> GifVideoFormatAsync(
        string path, MediaFormat inputFormat, CompressionOptions options, CancellationToken cancellationToken)
    {
        if (inputFormat != MediaFormat.Gif || options.Mode != CompressionMode.Lossy) return null;
        MediaFormat? format = options.AnimatedGifs switch
        {
            AnimatedGifOutput.Mp4 => MediaFormat.Mp4,
            AnimatedGifOutput.WebM => MediaFormat.WebM,
            _ => null,
        };
        if (format is not { } f || !_compressors.Any(c => c.IsAvailable && c.CanHandle(inputFormat, f, options))) return null;
        return await Imaging.WicCodec.ReadFrameCountAsync(path, cancellationToken).ConfigureAwait(false) > 1 ? f : null;
    }

    /// <summary>Compresses <paramref name="path"/> to <paramref name="outputFormat"/>.</summary>
    /// <param name="requested">A conversion the user asked for: kept even when larger.</param>
    private async Task<CompressionResult> ProcessAsAsync(
        string path,
        MediaFormat inputFormat,
        MediaFormat outputFormat,
        bool requested,
        CompressionOptions options,
        long originalSize,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        var isConversion = outputFormat != inputFormat;
        var isRequestedConversion = requested;
        var available = _compressors.Where(c => c.IsAvailable).ToList();
        var candidates = available.Where(c => c.CanHandle(inputFormat, outputFormat, options)).ToList();

        // An image larger than the size limit goes only to the engines that can scale it down; the others would
        // hand back a full-size file. Optimizers then polish the scaled result as usual.
        var resizeScale = await ResizeScaleAsync(path, inputFormat, options, cancellationToken).ConfigureAwait(false);
        if (resizeScale is not null) candidates = candidates.Where(c => c.CanResize).ToList();

        if (candidates.Count == 0)
        {
            if (MediaFormatInfo.KindOf(inputFormat) == MediaKind.Video && options.Mode == CompressionMode.Lossless)
                return new(path, CompressionStatus.Unsupported, originalSize, originalSize,
                    Message: L10n.T(
                        "Videos can only be compressed lossily: turn on “Lossy”.",
                        "Une vidéo ne se compresse qu'avec pertes : activez « Avec pertes »."));

            var mode = options.Mode == CompressionMode.Lossless ? L10n.T("lossless", "sans perte") : L10n.T("lossy", "avec pertes");
            var what = isConversion ? $"{inputFormat} → {outputFormat}" : $"{inputFormat} ({mode})";
            return new(path, CompressionStatus.Unsupported, originalSize, originalSize,
                Message: L10n.T($"No engine available for {what}.", $"Aucun moteur disponible pour {what}."));
        }

        var workDir = Path.Combine(Path.GetTempPath(), "SmashIt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var extension = MediaFormatInfo.ExtensionOf(outputFormat);
        var notes = new List<string>();
        try
        {
            // 1. Encoders: keep the smallest output.
            var errors = new List<RunError>();
            (string Path, long Size, ICompressor Compressor)? best = null;
            foreach (var compressor in candidates)
            {
                var job = new CompressionJob(path, inputFormat, Path.Combine(workDir, compressor.Id + extension), outputFormat, options)
                {
                    Progress = candidates.Count == 1 ? progress : null,
                    ResizeScale = resizeScale,
                };
                var size = await TryRunAsync(compressor, job, errors, cancellationToken).ConfigureAwait(false);
                if (size > 0 && (best is null || size < best.Value.Size))
                    best = (job.OutputPath, size, compressor);
            }

            if (best is null)
                // Every engine turned the file down (signed PDF, SVG font, animated PNG…): not supported, not an error.
                return new(path,
                    errors.Count > 0 && errors.All(e => e.Unsupported) ? CompressionStatus.Unsupported : CompressionStatus.Failed,
                    originalSize, originalSize,
                    Message: errors.Count > 0
                        ? string.Join(Environment.NewLine, errors.Select(e => e.Message))
                        : L10n.T("No result produced.", "Aucun résultat produit."));

            var (bestPath, bestSize, bestCompressor) = best.Value;
            var usedIds = new List<string> { bestCompressor.Id };

            // 2. Lossless optimizers on the winner (e.g. jpegtran after a lossy WIC encode).
            if (!bestCompressor.IsOptimizer)
            {
                var lossless = options with { Mode = CompressionMode.Lossless };
                foreach (var optimizer in available.Where(c => c.IsOptimizer && c.CanHandle(outputFormat, outputFormat, lossless)))
                {
                    var job = new CompressionJob(bestPath, outputFormat, Path.Combine(workDir, optimizer.Id + "-post" + extension), outputFormat, lossless);
                    var size = await TryRunAsync(optimizer, job, errors, cancellationToken).ConfigureAwait(false);
                    if (size > 0 && size < bestSize)
                    {
                        (bestPath, bestSize) = (job.OutputPath, size);
                        usedIds.Add(optimizer.Id);
                    }
                }
            }

            // 3. Metadata.
            if (MetadataTransfer.PipelineHandles(inputFormat, outputFormat))
            {
                var metadata = MetadataTransfer.Read(path, inputFormat);
                if (metadata is not null)
                {
                    var selected = MetadataTransfer.Select(
                        metadata, options, MetadataTransfer.BakesOrientation(inputFormat, outputFormat), outputFormat);
                    try
                    {
                        MetadataTransfer.Write(bestPath, outputFormat, selected);
                        bestSize = new FileInfo(bestPath).Length;
                    }
                    catch (InvalidDataException)
                    {
                        notes.Add(L10n.T("The metadata could not be written.", "Les métadonnées n'ont pas pu être écrites."));
                    }
                }
            }
            else if (!options.StripMetadata && MetadataTransfer.CanWrite(outputFormat) &&
                     MetadataTransfer.ReadDescriptive(path, inputFormat) is { } descriptive)
            {
                // HEIC/AVIF/WebP → JPEG/PNG: Windows decoded the pixels upright and in sRGB; EXIF (date, camera,
                // GPS…) and XMP follow, with the orientation reset.
                try
                {
                    MetadataTransfer.Write(bestPath, outputFormat,
                        MetadataTransfer.Select(descriptive, options, orientationBaked: true, outputFormat));
                    bestSize = new FileInfo(bestPath).Length;
                }
                catch (InvalidDataException)
                {
                    notes.Add(L10n.T("The metadata could not be written.", "Les métadonnées n'ont pas pu être écrites."));
                }
            }
            else if (!options.StripMetadata && !MetadataTransfer.CanPreserve(inputFormat, outputFormat))
            {
                notes.Add(L10n.T(
                    "Metadata can't be kept for this format yet.",
                    "Les métadonnées ne peuvent pas encore être conservées pour ce format."));
            }

            // 4. Output.
            var compressorId = string.Join("+", usedIds);
            if (!isRequestedConversion && bestSize >= originalSize)
            {
                string? copy = null;
                if (options is { OriginalHandling: OriginalHandling.KeepAlongside, CopyAlreadyOptimal: true,
                        KeptOriginalFolder: { Length: > 0 } copyFolder })
                {
                    var directory = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, copyFolder);
                    Directory.CreateDirectory(directory);
                    copy = FileOutput.UniquePath(Path.Combine(directory, Path.GetFileName(path)));
                    File.Copy(path, copy);
                }
                return new(path, CompressionStatus.AlreadyOptimal, originalSize, originalSize, copy,
                    CompressorId: compressorId, Message: Note(notes));
            }

            var status = isConversion ? CompressionStatus.Converted : CompressionStatus.Optimized;
            string finalPath;
            if (options.OriginalHandling == OriginalHandling.KeepAlongside)
            {
                var directory = Path.GetDirectoryName(path) ?? string.Empty;
                string name;
                if (options.KeptOriginalFolder is { Length: > 0 } folder)
                {
                    // "photos\compressed\photo.jpg": the folder already says what it is.
                    directory = Path.Combine(directory, folder);
                    Directory.CreateDirectory(directory);
                    name = Path.GetFileNameWithoutExtension(path) + extension;
                }
                else
                {
                    name = Path.GetFileNameWithoutExtension(path) + options.KeptOriginalSuffix + extension;
                }
                finalPath = FileOutput.UniquePath(Path.Combine(directory, name));
                File.Move(bestPath, finalPath);
            }
            else if (isConversion)
            {
                finalPath = FileOutput.UniquePath(Path.ChangeExtension(path, extension));
                File.Move(bestPath, finalPath);
                try
                {
                    FileOutput.DisposeOriginal(path, options.OriginalHandling);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    notes.Add(L10n.T(
                        $"The original could not be removed: {ex.Message}",
                        $"L'original n'a pas pu être supprimé : {ex.Message}"));
                }
            }
            else
            {
                FileOutput.Replace(path, bestPath, options.OriginalHandling);
                finalPath = path;
            }

            return new(path, status, originalSize, bestSize, finalPath, compressorId, Note(notes));
        }
        catch (OperationCanceledException)
        {
            return new(path, CompressionStatus.Cancelled, originalSize, originalSize);
        }
        catch (Exception ex)
        {
            return new(path, CompressionStatus.Failed, originalSize, originalSize, Message: ex.Message);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The scale that brings the image's long side down to <see cref="CompressionOptions.ImageMaxSide"/>, or
    /// <c>null</c>: not an image, lossless mode, no limit, already small enough, or a size Windows can't read
    /// (SVG has no pixels to scale).
    /// </summary>
    private static async Task<double?> ResizeScaleAsync(
        string path, MediaFormat format, CompressionOptions options, CancellationToken cancellationToken)
    {
        if (options.ImageMaxSide <= 0 || options.Mode != CompressionMode.Lossy) return null;
        if (MediaFormatInfo.KindOf(format) != MediaKind.Image || format == MediaFormat.Svg) return null;
        var size = await Imaging.WicCodec.ReadSizeAsync(path, cancellationToken).ConfigureAwait(false);
        return ScaleFor(size, options.ImageMaxSide);
    }

    /// <summary>The scale (0–1) bringing the long side of <paramref name="size"/> down to <paramref name="maxSide"/>.</summary>
    public static double? ScaleFor((uint Width, uint Height)? size, int maxSide)
    {
        if (size is not { } s || maxSide <= 0) return null;
        var longSide = Math.Max(s.Width, s.Height);
        return longSide > maxSide ? (double)maxSide / longSide : null;
    }

    /// <summary>Runs one compressor; returns the output size, or 0 when it failed (the error is collected).</summary>
    private static async Task<long> TryRunAsync(
        ICompressor compressor,
        CompressionJob job,
        List<RunError> errors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await compressor.CompressAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(new RunError(
                L10n.T($"{compressor.DisplayName}: {ex.Message}", $"{compressor.DisplayName} : {ex.Message}"),
                ex is NotSupportedException));
            return 0;
        }

        return File.Exists(job.OutputPath) ? new FileInfo(job.OutputPath).Length : 0;
    }

    /// <summary>What an engine said when it gave up; <paramref name="Unsupported"/> when it turned the file down.</summary>
    private sealed record RunError(string Message, bool Unsupported);

    private static string? Note(List<string> notes) => notes.Count > 0 ? string.Join(Environment.NewLine, notes) : null;
}
