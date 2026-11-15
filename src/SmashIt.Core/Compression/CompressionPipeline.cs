using SmashIt.Core.Formats;
using SmashIt.Core.IO;

namespace SmashIt.Core.Compression;

/// <summary>
/// Runs every compressor able to handle a file, keeps the smallest output, and only touches the original
/// when the result is actually smaller (or when a format conversion was requested).
/// </summary>
public sealed class CompressionPipeline(IEnumerable<ICompressor> compressors)
{
    private readonly IReadOnlyList<ICompressor> _compressors = compressors.ToList();

    public IReadOnlyList<ICompressor> Compressors => _compressors;

    public async Task<CompressionResult> ProcessAsync(
        string path,
        CompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        var originalSize = new FileInfo(path).Length;
        var inputFormat = FormatDetector.DetectFile(path);
        if (inputFormat == MediaFormat.Unknown)
            return new(path, CompressionStatus.Unsupported, originalSize, originalSize, Message: "Format non reconnu.");

        var outputFormat = options.TargetFormat ?? inputFormat;
        var isConversion = outputFormat != inputFormat;
        var candidates = _compressors.Where(c => c.CanHandle(inputFormat, outputFormat, options)).ToList();
        if (candidates.Count == 0)
        {
            var mode = options.Mode == CompressionMode.Lossless ? "sans perte" : "avec pertes";
            var what = isConversion ? $"{inputFormat} → {outputFormat}" : $"{inputFormat} ({mode})";
            return new(path, CompressionStatus.Unsupported, originalSize, originalSize,
                Message: $"Aucun moteur disponible pour {what}.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "SmashIt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            string? bestPath = null;
            ICompressor? bestCompressor = null;
            long bestSize = long.MaxValue;
            var errors = new List<string>();

            foreach (var compressor in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var output = Path.Combine(workDir, compressor.Id + MediaFormatInfo.ExtensionOf(outputFormat));
                try
                {
                    await compressor.CompressAsync(path, output, outputFormat, options, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{compressor.DisplayName} : {ex.Message}");
                    continue;
                }

                var size = File.Exists(output) ? new FileInfo(output).Length : 0;
                if (size > 0 && size < bestSize)
                {
                    bestPath = output;
                    bestSize = size;
                    bestCompressor = compressor;
                }
            }

            if (bestPath is null)
                return new(path, CompressionStatus.Failed, originalSize, originalSize,
                    Message: errors.Count > 0 ? string.Join(Environment.NewLine, errors) : "Aucun résultat produit.");

            if (isConversion)
            {
                var destination = FileOutput.UniquePath(
                    Path.ChangeExtension(path, MediaFormatInfo.ExtensionOf(outputFormat)));
                File.Move(bestPath, destination);
                return new(path, CompressionStatus.Converted, originalSize, bestSize, destination, bestCompressor!.Id);
            }

            if (bestSize >= originalSize)
                return new(path, CompressionStatus.AlreadyOptimal, originalSize, originalSize,
                    CompressorId: bestCompressor!.Id);

            string finalPath;
            switch (options.OriginalHandling)
            {
                case OriginalHandling.KeepAlongside:
                    var directory = Path.GetDirectoryName(path) ?? string.Empty;
                    var name = Path.GetFileNameWithoutExtension(path) + options.AlongsideSuffix + Path.GetExtension(path);
                    finalPath = FileOutput.UniquePath(Path.Combine(directory, name));
                    File.Move(bestPath, finalPath);
                    break;
                default:
                    FileOutput.Replace(path, bestPath, options.OriginalHandling == OriginalHandling.RecycleBin);
                    finalPath = path;
                    break;
            }

            return new(path, CompressionStatus.Optimized, originalSize, bestSize, finalPath, bestCompressor!.Id);
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
}
