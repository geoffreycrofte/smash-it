using System.Globalization;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Animated GIFs to video with ffmpeg: H.264 in MP4, which plays everywhere, or VP9 in WebM. A GIF stores every
/// frame as a separate 256-color picture; a video only stores what changes, in full color. A GIF made from a
/// clip often comes out more than 10 times lighter, a screen recording 50 times.
/// </summary>
/// <remarks>
/// The timing of each frame is kept (ffmpeg treats the very short delays browsers ignore the way they do). A
/// video doesn't loop by itself: the page or the player does it. GIFs with transparent areas are turned down
/// (<see cref="NotSupportedException"/>): a video would show them as a black box.
/// </remarks>
public sealed class FfmpegGifVideoCompressor() : ToolCompressor("ffmpeg")
{
    public override string Id => "ffmpeg";

    public override string DisplayName => "ffmpeg";

    public override bool CanResize => true;

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        input == MediaFormat.Gif && output is MediaFormat.Mp4 or MediaFormat.WebM && options.Mode == CompressionMode.Lossy;

    /// <summary>x264's CRF (lower is better) for a Smash It quality: 16 at 100, 23 (x264's default) at 70, 30 at 40.</summary>
    public static int H264Crf(int quality) => Math.Clamp((int)Math.Round(23 + (70 - Math.Clamp(quality, 1, 100)) * 7 / 30.0), 0, 51);

    /// <summary>VP9's CRF (0–63, lower is better): 24 at 100, 34 at 70, 44 at 40.</summary>
    public static int Vp9Crf(int quality) => Math.Clamp((int)Math.Round(34 + (70 - Math.Clamp(quality, 1, 100)) / 3.0), 0, 63);

    /// <summary>
    /// The video filter: the optional scale-down, conversion to the video colors (BT.709, 4:2:0) and square pixels,
    /// as browsers show GIFs. H.264 in 4:2:0 needs even sizes: an odd side loses its last pixel to the scaling.
    /// The color tags go on the frames themselves: the encoder takes them from there.
    /// </summary>
    public static string Filter(MediaFormat output, double? scale)
    {
        var s = (scale ?? 1).ToString("0.######", CultureInfo.InvariantCulture);
        var size = output == MediaFormat.Mp4
            ? $"w='max(2,trunc(iw*{s}/2)*2)':h='max(2,trunc(ih*{s}/2)*2)':"
            : scale is null ? "" : $"w='max(1,round(iw*{s}))':h='max(1,round(ih*{s}))':";
        return $"scale={size}flags=lanczos:out_color_matrix=bt709:out_range=tv,format=yuv420p,setsar=1," +
               "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv";
    }

    /// <summary>The ffmpeg command line that encodes a GIF to <see cref="CompressionJob.OutputFormat"/>.</summary>
    public static List<string> Arguments(CompressionJob job)
    {
        var options = job.Options;
        List<string> args =
        [
            "-hide_banner", "-nostdin", "-y", "-i", job.InputPath, "-map", "0:v:0",
            "-vf", Filter(job.OutputFormat, job.ResizeScale),
            // MP4 at a constant frame rate: an MP4 with B-frames loses how long its last frame lasts, and GIFs often
            // end on a long pause (or merge identical frames into one long one). Repeated frames cost almost nothing.
            // WebM keeps each frame's own duration.
            "-fps_mode", job.OutputFormat == MediaFormat.Mp4 ? "cfr" : "vfr",
        ];
        if (job.OutputFormat == MediaFormat.Mp4)
        {
            args.AddRange([
                "-c:v", "libx264", "-preset", options.Effort switch
                {
                    CompressionEffort.Fast => "veryfast",
                    CompressionEffort.Maximum => "veryslow",
                    _ => "slow",
                },
                "-crf", H264Crf(options.Quality).ToString(CultureInfo.InvariantCulture), "-profile:v", "high",
            ]);
        }
        else
        {
            args.AddRange([
                "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", Vp9Crf(options.Quality).ToString(CultureInfo.InvariantCulture),
                "-deadline", "good", "-cpu-used", options.Effort switch
                {
                    CompressionEffort.Fast => "4",
                    CompressionEffort.Maximum => "1",
                    _ => "2",
                },
                "-row-mt", "1",
            ]);
        }

        // A GIF's comments have no place in a video.
        args.AddRange(["-an", "-map_metadata", "-1"]);
        if (job.OutputFormat == MediaFormat.Mp4) args.AddRange(["-movflags", "+faststart"]);
        args.AddRange(["-progress", "pipe:1", "-nostats", "-loglevel", "error", job.OutputPath]);
        return args;
    }

    /// <summary>
    /// Decodes every frame as ffmpeg shows it (earlier frames showing through, as in a browser) and prints the
    /// lowest opacity of each: "lavfi.signalstats.YMIN=255" for an opaque frame.
    /// </summary>
    public static List<string> AnalysisArguments(string path) =>
    [
        "-hide_banner", "-nostdin", "-i", path, "-map", "0:v:0",
        "-vf", "format=rgba,alphaextract,signalstats,metadata=print:key=lavfi.signalstats.YMIN:file=-",
        "-f", "null", "-",
    ];

    /// <summary>True when one frame of the analysis output has a pixel that isn't fully opaque.</summary>
    public static bool HasTransparency(string analysis)
    {
        const string key = "lavfi.signalstats.YMIN=";
        foreach (var line in analysis.Split('\n'))
        {
            var start = line.IndexOf(key, StringComparison.Ordinal);
            if (start >= 0 && double.TryParse(line.AsSpan(start + key.Length).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && min < 255)
                return true;
        }
        return false;
    }

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var tool = ToolPath ?? throw new ExternalToolException(L10n.T("ffmpeg not found.", "ffmpeg est introuvable."));

        var analysis = await ExternalTool.RunRawAsync(tool, AnalysisArguments(job.InputPath), cancellationToken);
        if (analysis.ExitCode != 0) throw ExternalTool.Failure(tool, analysis);
        if (HasTransparency(analysis.StandardOutput))
            throw new NotSupportedException(L10n.T(
                "not converted to video, because the GIF has transparent areas a video can't show.",
                "pas de conversion en vidéo, car le GIF a des zones transparentes qu'une vidéo ne peut pas montrer."));

        var duration = FfmpegVideoCompressor.ParseProbe(analysis.StandardError).Duration;
        var result = await ExternalTool.RunRawAsync(
            tool, Arguments(job), cancellationToken, FfmpegVideoCompressor.ProgressReader(job.Progress, duration));
        if (result.ExitCode != 0) throw ExternalTool.Failure(tool, result);
    }
}
