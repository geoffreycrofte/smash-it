using System.IO.Compression;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Presentations;
using CompressionMode = SmashIt.Core.Compression.CompressionMode;

namespace SmashIt.Core.Compressors;

/// <summary>
/// PowerPoint and Keynote files: both are ZIP packages, and their weight is almost always in the pictures, videos and
/// sounds they hold. Each media file is compressed by the other engines, in its own format and under its own name, and
/// written back; nothing else in the package changes.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>PowerPoint (lossy mode): pictures far finer than the size they are shown at on their slide are scaled down to
/// the document resolution setting, as PowerPoint's own "Compress Pictures" does. Videos and sounds are recompressed in
/// their format (MP4 in H.264, MP3, M4A).</item>
/// <item>Keynote: only pictures, at their exact pixel size. Keynote records each picture's size in its Index archives,
/// which Smash It doesn't rewrite, so the dimensions and the file names never change.</item>
/// <item>The XML parts are re-deflated at the strongest level; entries the original stored uncompressed (Keynote
/// stores everything) stay uncompressed.</item>
/// </list>
/// </remarks>
public sealed class PresentationCompressor(CompressionPipeline media) : ICompressor
{
    public string Id => "package";

    public string DisplayName => L10n.T("presentation", "présentation");

    public bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        input is MediaFormat.Pptx or MediaFormat.Keynote && output == input;

    /// <summary>Whether a media part may be recompressed inside a package of this format.</summary>
    public static bool IsMediaPath(MediaFormat package, string path) => package == MediaFormat.Keynote
        ? path.StartsWith("Data/", StringComparison.Ordinal)
        : path.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The settings a media file of the package is compressed with: the document's mode, quality and metadata choice,
    /// always in its own format; videos in H.264, which PowerPoint plays everywhere.
    /// </summary>
    public static CompressionOptions MediaOptions(CompressionOptions document, int imageMaxSide) => document with
    {
        TargetFormat = null,
        AnimatedGifs = AnimatedGifOutput.Gif,
        VideoCodec = VideoCodec.H264,
        VideoMaxShortSide = 0,
        AudioContent = AudioContent.Music,
        ImageMaxSide = imageMaxSide,
        OriginalHandling = OriginalHandling.Overwrite,
        KeptOriginalFolder = null,
        CopyAlreadyOptimal = false,
    };

    public async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var work = job.OutputPath + ".media";
        Directory.CreateDirectory(work);
        try
        {
            await using var inputStream = new FileStream(job.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var input = new ZipArchive(inputStream, ZipArchiveMode.Read);
            if (input.Entries.Count == 0) throw new InvalidDataException(L10n.T("Empty package.", "Paquet vide."));

            var resize = job.InputFormat == MediaFormat.Pptx && job.Options.Mode == CompressionMode.Lossy && job.Options.PdfMaxImageDpi > 0;
            var shownSizes = resize ? SlideImageSizes.Read(input) : [];
            var mediaEntries = input.Entries.Where(e => IsMediaPath(job.InputFormat, e.FullName) && e.Length > 0).ToList();

            // 1. Each media file, compressed on its own.
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < mediaEntries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = mediaEntries[i];
                var replacement = await CompressMediaAsync(job, entry, Path.Combine(work, i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    resize && shownSizes.TryGetValue(entry.FullName, out var shown) ? shown : null, cancellationToken);
                if (replacement is not null) replacements[entry.FullName] = replacement;
                job.Progress?.Report(100.0 * (i + 1) / mediaEntries.Count);
            }

            // 2. The package, entry by entry in the same order, with the smaller media.
            await using (var outputStream = new FileStream(job.OutputPath, FileMode.Create, FileAccess.Write))
            using (var output = new ZipArchive(outputStream, ZipArchiveMode.Create))
            {
                foreach (var entry in input.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stored = entry.Length > 0 && entry.CompressedLength >= entry.Length;
                    var copy = output.CreateEntry(entry.FullName, stored ? CompressionLevel.NoCompression : CompressionLevel.SmallestSize);
                    copy.LastWriteTime = entry.LastWriteTime;
                    await using var target = copy.Open();
                    if (replacements.TryGetValue(entry.FullName, out var file))
                    {
                        await using var source = File.OpenRead(file);
                        await source.CopyToAsync(target, cancellationToken);
                    }
                    else
                    {
                        await using var source = entry.Open();
                        await source.CopyToAsync(target, cancellationToken);
                    }
                }
            }

            // 3. The result must read back whole, with every entry of the original.
            await using var checkStream = File.OpenRead(job.OutputPath);
            using var check = new ZipArchive(checkStream, ZipArchiveMode.Read);
            if (check.Entries.Count != input.Entries.Count)
                throw new InvalidDataException(L10n.T("The rewritten package lost entries.", "Le paquet réécrit a perdu des éléments."));
            foreach (var entry in check.Entries)
            {
                await using var read = entry.Open();
                await read.CopyToAsync(Stream.Null, cancellationToken);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new NotSupportedException(L10n.T($"Unreadable package: {ex.Message}", $"Paquet illisible : {ex.Message}"));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Compresses one media file; returns the path of the smaller version, or null when there is none (unknown or
    /// unsupported format, no gain, or a format the package must keep).
    /// </summary>
    private async Task<string?> CompressMediaAsync(
        CompressionJob job, ZipArchiveEntry entry, string folder, SlideImageSizes.Inches? shown, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(entry.FullName));
        await using (var source = entry.Open())
        await using (var target = File.Create(path))
            await source.CopyToAsync(target, cancellationToken);

        var format = FormatDetector.DetectFile(path);
        var kind = MediaFormatInfo.KindOf(format);
        // Only what stays in its own format: a WAV would become a FLAC, a MOV an MP4. Keynote: pictures only.
        if (MediaFormatInfo.DefaultOutputFor(format) != format) return null;
        if (kind is not (MediaKind.Image or MediaKind.Video or MediaKind.Audio)) return null;
        if (job.InputFormat == MediaFormat.Keynote && kind != MediaKind.Image) return null;

        var maxSide = 0;
        if (kind == MediaKind.Image && shown is not null && await WicCodec.ReadSizeAsync(path, cancellationToken) is { } size)
            maxSide = SlideImageSizes.TargetLongSide(size.Width, size.Height, shown, job.Options.PdfMaxImageDpi);

        var result = await media.ProcessAsync(path, MediaOptions(job.Options, maxSide), cancellationToken);
        return result.Status == CompressionStatus.Optimized && result.OutputPath == path ? path : null;
    }
}
