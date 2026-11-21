using System.Globalization;
using System.Text.RegularExpressions;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Sound with ffmpeg: MP3 (LAME), AAC in M4A, Ogg Vorbis, Opus, and FLAC.
/// </summary>
/// <remarks>
/// <para>Lossless mode never re-encodes a lossy file: WAV, AIFF and FLAC become FLAC at its strongest setting (every
/// sample kept), and MP3, M4A, Ogg and Opus files only lose their tags and cover art when metadata is removed.</para>
/// <para>Lossy mode encodes at a bitrate taken from the quality and the content: music stays stereo, voice becomes
/// mono at about a third of the bitrate. The bitrates follow the codec authors' recommendations (Opus: 96 kbit/s for
/// stereo music, 32 kbit/s for speech; LAME: V2 for music). A lossy file that is already at or below that bitrate in
/// the same format is not re-encoded, which would only lose quality: its sound is copied as it is.</para>
/// </remarks>
public sealed partial class FfmpegAudioCompressor() : ToolCompressor("ffmpeg.exe")
{
    private static readonly HashSet<MediaFormat> Writable =
        [MediaFormat.Mp3, MediaFormat.M4a, MediaFormat.Flac, MediaFormat.Ogg, MediaFormat.Opus];

    public override string Id => "ffmpeg";

    public override string DisplayName => "ffmpeg";

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options)
    {
        if (MediaFormatInfo.KindOf(input) != MediaKind.Audio || !Writable.Contains(output)) return false;
        // FLAC only from lossless sound: a lossy file would just get larger.
        if (output == MediaFormat.Flac) return MediaFormatInfo.IsLosslessAudio(input);
        // Lossless mode, lossy format: the sound is copied, only the tags can go.
        return options.Mode == CompressionMode.Lossy || output == MediaFormatInfo.DefaultOutputFor(input);
    }

    /// <summary>What the encoding needs to know about the source.</summary>
    public sealed record SourceInfo(TimeSpan Duration, string Codec, int Channels, int Bitrate);

    /// <summary>The sound encoding chosen for a job.</summary>
    public enum Method
    {
        /// <summary>The sound is copied untouched (lossless mode, or a lossy file already light enough).</summary>
        Copy,

        /// <summary>Lossless FLAC.</summary>
        Flac,

        /// <summary>A lossy encoder at <see cref="Plan.Bitrate"/> or <see cref="Plan.VbrQuality"/>.</summary>
        Encode,
    }

    /// <param name="Bitrate">Target bitrate in bit/s (constant or average), when <see cref="VbrQuality"/> is null.</param>
    /// <param name="VbrQuality">MP3 or Vorbis variable-bitrate quality (LAME's V0–V9, Vorbis's −1–10).</param>
    public sealed record Plan(Method Method, int Bitrate = 0, double? VbrQuality = null, bool Mono = false);

    /// <summary>Linear interpolation of a setting through its values at quality 100, 70, 40 and 1.</summary>
    private static double Curve(int quality, double at100, double at70, double at40, double at1)
    {
        var q = Math.Clamp(quality, 1, 100);
        return q >= 70 ? at70 + (at100 - at70) * (q - 70) / 30.0
            : q >= 40 ? at40 + (at70 - at40) * (q - 40) / 30.0
            : at1 + (at40 - at1) * (q - 1) / 39.0;
    }

    /// <summary>Kilobits per second for a lossy codec, quality and content (stereo music, mono voice).</summary>
    public static int TargetKbps(MediaFormat output, int quality, AudioContent content) => (int)Math.Round(output switch
    {
        MediaFormat.Opus => content == AudioContent.Voice ? Curve(quality, 48, 32, 24, 12) : Curve(quality, 160, 96, 64, 32),
        MediaFormat.M4a => content == AudioContent.Voice ? Curve(quality, 96, 64, 48, 32) : Curve(quality, 256, 160, 112, 64),
        MediaFormat.Mp3 => content == AudioContent.Voice ? Curve(quality, 96, 64, 48, 32) : VbrKbps(Mp3Vbr(quality)),
        MediaFormat.Ogg => content == AudioContent.Voice ? Curve(quality, 96, 64, 48, 32) : Curve(quality, 256, 160, 112, 64),
        _ => 0,
    });

    /// <summary>LAME's V setting for music: V0 at quality 100, V2 at 70 (transparent for most listeners), V5 at 40, V9 at 1.</summary>
    public static double Mp3Vbr(int quality) => Math.Round(Curve(quality, 0, 2, 5, 9), 1);

    /// <summary>Vorbis's quality setting for music: 8 at quality 100, 5 at 70 (about 160 kbit/s), 3 at 40, 0 at 1.</summary>
    public static double VorbisQuality(int quality) => Math.Round(Curve(quality, 8, 5, 3, 0), 1);

    /// <summary>Typical stereo bitrate of a LAME V setting, in kbit/s (LAME's documentation).</summary>
    private static double VbrKbps(double v)
    {
        double[] kbps = [245, 225, 190, 175, 165, 130, 115, 100, 85, 65];
        var low = (int)Math.Clamp(Math.Floor(v), 0, 9);
        var high = Math.Min(low + 1, 9);
        return kbps[low] + (kbps[high] - kbps[low]) * (v - low);
    }

    private static readonly Dictionary<MediaFormat, string[]> CodecsOf = new()
    {
        [MediaFormat.Mp3] = ["mp3"],
        [MediaFormat.M4a] = ["aac"],
        [MediaFormat.Ogg] = ["vorbis"],
        [MediaFormat.Opus] = ["opus"],
    };

    /// <summary>How a job is encoded, given what the source holds.</summary>
    public static Plan Decide(CompressionJob job, SourceInfo source)
    {
        if (job.OutputFormat == MediaFormat.Flac) return new Plan(Method.Flac);
        var options = job.Options;
        if (options.Mode == CompressionMode.Lossless) return new Plan(Method.Copy);

        var voice = options.AudioContent == AudioContent.Voice;
        var mono = voice || source.Channels == 1;
        var kbps = TargetKbps(job.OutputFormat, options.Quality, options.AudioContent);
        // Mono music needs about 60 % of the stereo bitrate.
        if (!voice && source.Channels == 1) kbps = (int)Math.Round(kbps * 0.6);
        var target = kbps * 1000;

        var lossySource = !MediaFormatInfo.IsLosslessAudio(job.InputFormat) && source.Bitrate > 0;
        var sameCodec = CodecsOf.TryGetValue(job.OutputFormat, out var codecs) && codecs.Contains(source.Codec);
        // Already as light as the target in the same format: re-encoding would only lose quality.
        if (lossySource && sameCodec && target >= source.Bitrate * 0.85) return new Plan(Method.Copy);
        // Never more bits than the lossy source has.
        if (lossySource && target > source.Bitrate * 0.9)
            return new Plan(Method.Encode, Bitrate: (int)(source.Bitrate * 0.9 / 1000) * 1000, Mono: mono);

        double? vbr = !voice && job.OutputFormat == MediaFormat.Mp3 ? Mp3Vbr(options.Quality)
            : !voice && job.OutputFormat == MediaFormat.Ogg && !mono ? VorbisQuality(options.Quality)
            : null;
        return new Plan(Method.Encode, vbr is null ? target : 0, vbr, mono);
    }

    /// <summary>The ffmpeg command line for a job.</summary>
    public static List<string> Arguments(CompressionJob job, SourceInfo source)
    {
        var options = job.Options;
        var plan = Decide(job, source);
        var output = job.OutputFormat;
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-i", job.InputPath, "-map", "0:a:0"];

        // Cover art travels with the tags, in the formats that can hold it (not Ogg).
        var keepCover = !options.StripMetadata && output is MediaFormat.Mp3 or MediaFormat.M4a or MediaFormat.Flac;
        if (keepCover) args.AddRange(["-map", "0:v?", "-c:v", "copy", "-disposition:v", "attached_pic"]);

        string Level(string fast, string balanced, string maximum) => options.Effort switch
        {
            CompressionEffort.Fast => fast,
            CompressionEffort.Maximum => maximum,
            _ => balanced,
        };
        var bitrate = plan.Bitrate.ToString(CultureInfo.InvariantCulture);

        switch (plan.Method)
        {
            case Method.Copy:
                args.AddRange(["-c:a", "copy"]);
                break;
            case Method.Flac:
                args.AddRange(["-c:a", "flac", "-compression_level", Level("5", "8", "12")]);
                break;
            default:
                if (plan.Mono) args.AddRange(["-ac", "1"]);
                else if (source.Channels > 2) args.AddRange(["-ac", "2"]);
                switch (output)
                {
                    case MediaFormat.Mp3:
                        args.AddRange(["-c:a", "libmp3lame", "-compression_level", Level("7", "3", "0")]);
                        args.AddRange(plan.VbrQuality is { } v
                            ? ["-q:a", v.ToString(CultureInfo.InvariantCulture)]
                            : ["-b:a", bitrate]);
                        break;
                    case MediaFormat.M4a:
                        args.AddRange(["-c:a", "aac", "-b:a", bitrate]);
                        break;
                    case MediaFormat.Ogg:
                        args.AddRange(["-c:a", "libvorbis"]);
                        args.AddRange(plan.VbrQuality is { } q
                            ? ["-q:a", q.ToString(CultureInfo.InvariantCulture)]
                            : ["-b:a", bitrate]);
                        break;
                    default:
                        args.AddRange([
                            "-c:a", "libopus", "-b:a", bitrate, "-vbr", "on",
                            "-application", options.AudioContent == AudioContent.Voice ? "voip" : "audio",
                            "-compression_level", Level("5", "10", "10"),
                        ]);
                        break;
                }
                break;
        }

        args.AddRange(options.StripMetadata
            ? ["-map_metadata", "-1", "-map_chapters", "-1"]
            : ["-map_metadata", "0"]);
        if (output == MediaFormat.Mp3 && !options.StripMetadata) args.AddRange(["-id3v2_version", "3"]);
        if (output == MediaFormat.M4a) args.AddRange(["-movflags", "+faststart"]);
        args.AddRange(["-progress", "pipe:1", "-nostats", "-loglevel", "error", job.OutputPath]);
        return args;
    }

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var tool = ToolPath ?? throw new ExternalToolException(L10n.T("ffmpeg not found.", "ffmpeg est introuvable."));

        // Without an output, ffmpeg describes the input on standard error and exits with code 1.
        var probe = await ExternalTool.RunRawAsync(tool, ["-hide_banner", "-nostdin", "-i", job.InputPath], cancellationToken);
        var source = ParseProbe(probe.StandardError)
            ?? throw new NotSupportedException(L10n.T("No sound in this file.", "Aucun son dans ce fichier."));
        // FLAC stores integers: floating-point samples would be rounded.
        if (job.OutputFormat == MediaFormat.Flac && source.Codec.StartsWith("pcm_f", StringComparison.Ordinal))
            throw new NotSupportedException(L10n.T(
                "floating-point samples can't be stored in FLAC without rounding.",
                "les échantillons en virgule flottante ne tiennent pas dans un FLAC sans arrondi."));

        var result = await ExternalTool.RunRawAsync(
            tool, Arguments(job, source), cancellationToken, FfmpegVideoCompressor.ProgressReader(job.Progress, source.Duration));
        if (result.ExitCode != 0) throw ExternalTool.Failure(tool, result);
    }

    /// <summary>The duration and the first sound stream, from ffmpeg's description of a file; <c>null</c> without sound.</summary>
    public static SourceInfo? ParseProbe(string description)
    {
        var audio = AudioPattern().Match(description);
        if (!audio.Success) return null;
        var duration = FfmpegVideoCompressor.ParseProbe(description).Duration;
        var line = audio.Value;
        var channels = ChannelsPattern().Match(line) is { Success: true } c
            ? c.Groups[1].Value switch
            {
                "mono" => 1,
                "stereo" => 2,
                var layout when ChannelCountPattern().Match(layout) is { Success: true } n => int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture),
                var layout => layout.StartsWith("5.1", StringComparison.Ordinal) ? 6 : layout.StartsWith("7.1", StringComparison.Ordinal) ? 8 : 2,
            }
            : 2;
        // The stream's bitrate, else the whole file's (some MP3s only give the latter).
        var bitrate = BitratePattern().Match(line) is { Success: true } b ? b.Groups[1].Value
            : FileBitratePattern().Match(description) is { Success: true } f ? f.Groups[1].Value
            : "0";
        return new SourceInfo(duration, audio.Groups[1].Value, channels, int.Parse(bitrate, CultureInfo.InvariantCulture) * 1000);
    }

    [GeneratedRegex(@"Stream #\S+.*?: Audio: (\w+)[^\r\n]*")]
    private static partial Regex AudioPattern();

    [GeneratedRegex(@"Hz, ([^,\r\n]+)")]
    private static partial Regex ChannelsPattern();

    [GeneratedRegex(@"^(\d+) channels")]
    private static partial Regex ChannelCountPattern();

    [GeneratedRegex(@"(\d+) kb/s")]
    private static partial Regex BitratePattern();

    [GeneratedRegex(@"Duration: [^\r\n]*?bitrate: (\d+) kb/s")]
    private static partial Regex FileBitratePattern();
}
