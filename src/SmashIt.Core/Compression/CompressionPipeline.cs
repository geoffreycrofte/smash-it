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
        var isConversion = outputFormat != inputFormat;
        // A conversion the user asked for is kept even if larger; one forced on us (MKV → MP4) is not.
        var isRequestedConversion = isConversion && options.TargetFormat == outputFormat;
        var available = _compressors.Where(c => c.IsAvailable).ToList();
        var candidates = available.Where(c => c.CanHandle(inputFormat, outputFormat, options)).ToList();
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
            var errors = new List<string>();
            (string Path, long Size, ICompressor Compressor)? best = null;
            foreach (var compressor in candidates)
            {
                var job = new CompressionJob(path, inputFormat, Path.Combine(workDir, compressor.Id + extension), outputFormat, options)
                {
                    Progress = candidates.Count == 1 ? progress : null,
                };
                var size = await TryRunAsync(compressor, job, errors, cancellationToken).ConfigureAwait(false);
                if (size > 0 && (best is null || size < best.Value.Size))
                    best = (job.OutputPath, size, compressor);
            }

            if (best is null)
                return new(path, CompressionStatus.Failed, originalSize, originalSize,
                    Message: errors.Count > 0 ? string.Join(Environment.NewLine, errors) : L10n.T("No result produced.", "Aucun résultat produit."));

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
            else if (!options.StripMetadata && !MetadataTransfer.CanPreserve(inputFormat, outputFormat))
            {
                notes.Add(L10n.T(
                    "Metadata can't be kept for this format yet.",
                    "Les métadonnées ne peuvent pas encore être conservées pour ce format."));
            }

            // 4. Output.
            var compressorId = string.Join("+", usedIds);
            if (!isRequestedConversion && bestSize >= originalSize)
                return new(path, CompressionStatus.AlreadyOptimal, originalSize, originalSize,
                    CompressorId: compressorId, Message: Note(notes));

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

    /// <summary>Runs one compressor; returns the output size, or 0 when it failed (the error is collected).</summary>
    private static async Task<long> TryRunAsync(
        ICompressor compressor,
        CompressionJob job,
        List<string> errors,
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
            errors.Add(L10n.T($"{compressor.DisplayName}: {ex.Message}", $"{compressor.DisplayName} : {ex.Message}"));
            return 0;
        }

        return File.Exists(job.OutputPath) ? new FileInfo(job.OutputPath).Length : 0;
    }

    private static string? Note(List<string> notes) => notes.Count > 0 ? string.Join(Environment.NewLine, notes) : null;
}
