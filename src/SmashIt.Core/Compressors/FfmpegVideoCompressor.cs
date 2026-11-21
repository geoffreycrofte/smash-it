using System.Globalization;
using System.Text.RegularExpressions;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Compressors;

/// <summary>
/// AV1 videos with ffmpeg and SVT-AV1 (GPL build), in MP4 with AAC sound. Constant quality: the quality setting
/// picks a CRF and the size follows the content, where Media Foundation targets a bitrate.
/// </summary>
/// <remarks>
/// Phone videos come out upright (ffmpeg applies the rotation). The video is encoded in 10 bits, which avoids
/// banding in skies and gradients at no cost in size, and keeps the color tags of HDR sources. Metadata (dates,
/// location) is kept or removed with the "Remove metadata" setting.
/// </remarks>
public sealed partial class FfmpegVideoCompressor() : ToolCompressor("ffmpeg.exe")
{
    private static readonly HashSet<MediaFormat> Readable =
        [MediaFormat.Mp4, MediaFormat.Mov, MediaFormat.Mkv, MediaFormat.Avi, MediaFormat.WebM];

    public override string Id => "ffmpeg";

    public override string DisplayName => "ffmpeg";

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        Readable.Contains(input) && output == MediaFormat.Mp4 && options.Mode == CompressionMode.Lossy
        && options.VideoCodec == VideoCodec.Av1;

    /// <summary>
    /// SVT-AV1's CRF (0–63, lower is better) for a Smash It quality: 20 at 100, 34 at 70 (VMAF about 93 on our test
    /// clips, differences hard to see), 48 at 40.
    /// </summary>
    public static int Crf(int quality) => Math.Clamp((int)Math.Round(20 + (100 - Math.Clamp(quality, 1, 100)) * 14 / 30.0), 10, 63);

    /// <summary>
    /// SVT-AV1 preset (lower is slower and smaller): 10 fast, 7 balanced, 6 maximum. From 7 down the encoder looks
    /// 32 frames ahead instead of 16: 25 % smaller than preset 8 at the same quality in our tests, for 40 % more time.
    /// </summary>
    public static int Preset(CompressionEffort effort) => effort switch
    {
        CompressionEffort.Fast => 10,
        CompressionEffort.Maximum => 6,
        _ => 7,
    };

    /// <summary>
    /// The scale filter that brings the short side down to <paramref name="maxShortSide"/> (never up), the other side
    /// following in proportion, rounded to an even number. Applied after rotation, so portrait videos work the same.
    /// </summary>
    public static string ScaleFilter(int maxShortSide) =>
        $"scale=w='if(lte(iw,ih),min(iw,{maxShortSide}),-2)':h='if(lte(iw,ih),-2,min(ih,{maxShortSide}))':flags=lanczos";

    /// <summary>The ffmpeg command line for a job, given what the source holds.</summary>
    public static List<string> Arguments(CompressionJob job, SourceInfo source)
    {
        var options = job.Options;
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-i", job.InputPath, "-map", "0:v:0", "-map", "0:a:0?"];
        if (options.VideoMaxShortSide > 0) args.AddRange(["-vf", ScaleFilter(options.VideoMaxShortSide)]);
        args.AddRange([
            "-c:v", "libsvtav1", "-preset", Preset(options.Effort).ToString(CultureInfo.InvariantCulture),
            "-crf", Crf(options.Quality).ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p10le",
            "-svtav1-params", "tune=0",
        ]);

        // AAC up to 192 kbit/s is copied as is; anything else becomes AAC at the source bitrate, 128–160 kbit/s.
        if (source.AudioCodec is "aac" && source.AudioBitrate is > 0 and <= 192_000) args.AddRange(["-c:a", "copy"]);
        else if (source.AudioCodec is not null)
        {
            var bitrate = source.AudioBitrate is > 0 ? Math.Clamp(source.AudioBitrate, 128_000, 160_000) : 160_000;
            args.AddRange(["-c:a", "aac", "-b:a", bitrate.ToString(CultureInfo.InvariantCulture)]);
        }

        args.AddRange(options.StripMetadata
            ? ["-map_metadata", "-1", "-map_chapters", "-1", "-movflags", "+faststart"]
            : ["-map_metadata", "0", "-movflags", "+faststart+use_metadata_tags"]);
        args.AddRange(["-progress", "pipe:1", "-nostats", "-loglevel", "error", job.OutputPath]);
        return args;
    }

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var tool = ToolPath ?? throw new ExternalToolException(L10n.T("ffmpeg not found.", "ffmpeg est introuvable."));
        var source = await ProbeAsync(tool, job.InputPath, cancellationToken);

        var result = await ExternalTool.RunRawAsync(
            tool, Arguments(job, source), cancellationToken, ProgressReader(job.Progress, source.Duration));
        if (result.ExitCode != 0) throw ExternalTool.Failure(tool, result);
    }

    /// <summary>
    /// Turns the lines of <c>-progress pipe:1</c> into a percentage: "out_time_us=12345678" is the time encoded so
    /// far, in microseconds.
    /// </summary>
    internal static Action<string>? ProgressReader(IProgress<double>? progress, TimeSpan duration) =>
        progress is null || duration <= TimeSpan.Zero
            ? null
            : line =>
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan("out_time_us=".Length), out var us) && us > 0)
                    progress.Report(Math.Clamp(us / 10.0 / duration.TotalMilliseconds, 0, 100));
            };

    /// <summary>What the encoding needs to know about the source.</summary>
    public sealed record SourceInfo(TimeSpan Duration, string? AudioCodec, int AudioBitrate);

    /// <summary>Reads the duration and the first audio stream from ffmpeg's description of the file.</summary>
    public static SourceInfo ParseProbe(string description)
    {
        var duration = DurationPattern().Match(description) is { Success: true } d
            ? new TimeSpan(0, int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture), 0)
              + TimeSpan.FromSeconds(double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture))
            : TimeSpan.Zero;
        var audio = AudioPattern().Match(description);
        if (!audio.Success) return new SourceInfo(duration, null, 0);
        var bitrate = AudioBitratePattern().Match(audio.Value) is { Success: true } b
            ? int.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture) * 1000
            : 0;
        return new SourceInfo(duration, audio.Groups[1].Value, bitrate);
    }

    private static async Task<SourceInfo> ProbeAsync(string tool, string path, CancellationToken cancellationToken)
    {
        // Without an output, ffmpeg describes the input on standard error and exits with code 1.
        var result = await ExternalTool.RunRawAsync(tool, ["-hide_banner", "-nostdin", "-i", path], cancellationToken);
        if (!result.StandardError.Contains("Video:", StringComparison.Ordinal))
            throw new NotSupportedException(L10n.T("No video track in this file.", "Aucune piste vidéo dans ce fichier."));
        return ParseProbe(result.StandardError);
    }

    [GeneratedRegex(@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"Stream #\S+.*?: Audio: (\w+)[^\r\n]*")]
    private static partial Regex AudioPattern();

    [GeneratedRegex(@"(\d+) kb/s")]
    private static partial Regex AudioBitratePattern();
}
