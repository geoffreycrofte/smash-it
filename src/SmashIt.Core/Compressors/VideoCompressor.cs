using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Re-encodes videos to MP4 (H.264 or HEVC + AAC) with Media Foundation, built into Windows,
/// using the GPU encoder when there is one.
/// </summary>
/// <remarks>
/// The video bitrate comes from the quality setting, the resolution and the frame rate, and never exceeds
/// the source bitrate. The frame rate is kept; the resolution too, unless
/// <see cref="CompressionOptions.VideoMaxShortSide"/> brings it down (a 4K phone video to 1080p, say), which
/// lowers the bitrate with it. Rotated videos (phones) come out upright: the transcoder applies the rotation
/// flag to the pixels.
/// </remarks>
public sealed class VideoCompressor : ICompressor
{
    private static readonly HashSet<MediaFormat> Readable =
        [MediaFormat.Mp4, MediaFormat.Mov, MediaFormat.Mkv, MediaFormat.Avi, MediaFormat.WebM];

    public string Id => "mediafoundation";

    public string DisplayName => "Media Foundation";

    public bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        Readable.Contains(input) && output == MediaFormat.Mp4 && options.Mode == CompressionMode.Lossy;

    /// <summary>
    /// Bits per pixel per frame for H.264: 0.015 at quality 1, about 0.11 at 85, 0.15 at 100
    /// (1080p30 at quality 85 ≈ 7 Mbit/s). HEVC needs about 60 % of that.
    /// </summary>
    public static uint TargetVideoBitrate(int quality, VideoCodec codec, uint width, uint height, double frameRate, uint sourceBitrate)
    {
        var q = Math.Clamp(quality, 1, 100) / 100.0;
        var bitsPerPixel = (0.015 + q * q * 0.135) * (codec == VideoCodec.Hevc ? 0.6 : 1.0);
        var target = bitsPerPixel * width * height * frameRate;
        if (sourceBitrate > 0) target = Math.Min(target, sourceBitrate * 0.95);
        return (uint)Math.Max(100_000, target);
    }

    public async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(job.InputPath)).AsTask(cancellationToken);
        var sourceProfile = await MediaEncodingProfile.CreateFromFileAsync(source).AsTask(cancellationToken);
        var sourceVideo = sourceProfile.Video ?? throw new NotSupportedException(L10n.T("No video track in this file.", "Aucune piste vidéo dans ce fichier."));
        var properties = await source.Properties.GetVideoPropertiesAsync().AsTask(cancellationToken);

        // Media Foundation applies the source rotation itself (portrait phone videos come out upright, with no
        // rotation flag). The profile must therefore describe the frame as stored, before rotation.
        // Scaling the short side works the same for landscape and portrait frames.
        var (width, height) = OutputSize(sourceVideo.Width, sourceVideo.Height, job.Options.VideoMaxShortSide);

        var frameRate = sourceVideo.FrameRate.Denominator > 0 && sourceVideo.FrameRate.Numerator > 0
            ? (sourceVideo.FrameRate.Numerator, sourceVideo.FrameRate.Denominator)
            : (30u, 1u);
        var audioBitrate = sourceProfile.Audio is { Bitrate: > 0 } a ? Math.Min(a.Bitrate, 160_000u) : 128_000u;
        var sourceVideoBitrate = SourceVideoBitrate(
            sourceVideo.Bitrate, properties.Bitrate, new FileInfo(job.InputPath).Length, properties.Duration, audioBitrate);

        var codec = job.Options.VideoCodec;
        var profile = codec == VideoCodec.Hevc
            ? MediaEncodingProfile.CreateHevc(VideoEncodingQuality.HD1080p)
            : MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = width;
        profile.Video.Height = height;
        profile.Video.FrameRate.Numerator = frameRate.Item1;
        profile.Video.FrameRate.Denominator = frameRate.Item2;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = TargetVideoBitrate(
            job.Options.Quality, codec, width, height, (double)frameRate.Item1 / frameRate.Item2, sourceVideoBitrate);

        if (sourceProfile.Audio is { } audio)
        {
            var channels = Math.Clamp(audio.ChannelCount, 1u, 2u);
            var sampleRate = audio.SampleRate is 44_100 or 48_000 ? audio.SampleRate : 48_000u;
            profile.Audio = AudioEncodingProperties.CreateAac(sampleRate, channels, audioBitrate);
        }
        else
        {
            profile.Audio = null;
        }

        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(job.OutputPath))!).AsTask(cancellationToken);
        var destination = await folder.CreateFileAsync(Path.GetFileName(job.OutputPath), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken);

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile).AsTask(cancellationToken);
        if (!prepared.CanTranscode)
        {
            throw new NotSupportedException(prepared.FailureReason switch
            {
                TranscodeFailureReason.CodecNotFound => codec == VideoCodec.Hevc
                    ? L10n.T(
                        "Encoder or decoder not found (HEVC: install “HEVC Video Extensions” from the Microsoft Store).",
                        "Encodeur ou décodeur introuvable (HEVC : installez « Extensions vidéo HEVC » depuis le Microsoft Store).")
                    : L10n.T("Windows doesn't support this video's codec.", "Codec de la vidéo non pris en charge par Windows."),
                TranscodeFailureReason.InvalidProfile => L10n.T("Windows refused the encoding settings.", "Réglages d'encodage refusés par Windows."),
                _ => L10n.T("Windows can't transcode this video.", "Windows ne peut pas transcoder cette vidéo."),
            });
        }

        var operation = prepared.TranscodeAsync();
        await (job.Progress is { } progress
            ? operation.AsTask(cancellationToken, progress)
            : operation.AsTask(cancellationToken));
    }

    private static uint Even(uint value) => Math.Max(2, value & ~1u);

    /// <summary>
    /// The encoded frame size: the source size, or scaled down so that its short side is
    /// <paramref name="maxShortSide"/> (never up), with the same proportions. Encoders need even sizes.
    /// </summary>
    public static (uint Width, uint Height) OutputSize(uint width, uint height, int maxShortSide)
    {
        var shortSide = Math.Min(width, height);
        if (maxShortSide <= 0 || shortSide <= maxShortSide) return (Even(width), Even(height));
        var scale = (double)maxShortSide / shortSide;
        return (Even((uint)Math.Round(width * scale)), Even((uint)Math.Round(height * scale)));
    }

    /// <summary>
    /// The source video bitrate: from the stream, else the container, else file size ÷ duration
    /// (MKV files often report none).
    /// </summary>
    public static uint SourceVideoBitrate(uint streamBitrate, uint containerBitrate, long fileSize, TimeSpan duration, uint audioBitrate)
    {
        if (streamBitrate > 0) return streamBitrate;
        var total = containerBitrate > 0
            ? containerBitrate
            : duration.TotalSeconds > 0.1 ? (uint)Math.Min(uint.MaxValue, fileSize * 8 / duration.TotalSeconds) : 0u;
        return total > audioBitrate ? total - audioBitrate : total;
    }
}
