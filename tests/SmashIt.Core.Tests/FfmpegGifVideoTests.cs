using System.Text.RegularExpressions;
using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>Animated GIFs to MP4 and WebM videos through ffmpeg.</summary>
public class FfmpegGifVideoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions ToMp4 = new()
    {
        Mode = CompressionMode.Lossy, Quality = 70, AnimatedGifs = AnimatedGifOutput.Mp4,
        OriginalHandling = OriginalHandling.KeepAlongside,
    };

    private static string RequireFfmpeg() => RequireTool("ffmpeg.exe");

    private static string RequireTool(string name)
    {
        var path = ExternalTool.Find(name);
        if (path is null) Assert.Skip($"{name} absent : lancez tools\\fetch-tools.ps1.");
        return path;
    }

    [Theory]
    [InlineData(100, 16, 24)]
    [InlineData(70, 23, 34)]
    [InlineData(40, 30, 44)]
    [InlineData(1, 39, 57)]
    public void Quality_maps_to_a_crf(int quality, int h264, int vp9)
    {
        Assert.Equal(h264, FfmpegGifVideoCompressor.H264Crf(quality));
        Assert.Equal(vp9, FfmpegGifVideoCompressor.Vp9Crf(quality));
    }

    [Fact]
    public void Only_lossy_gifs_become_videos()
    {
        var engine = new FfmpegGifVideoCompressor();
        Assert.True(engine.CanHandle(MediaFormat.Gif, MediaFormat.Mp4, ToMp4));
        Assert.True(engine.CanHandle(MediaFormat.Gif, MediaFormat.WebM, ToMp4));
        Assert.False(engine.CanHandle(MediaFormat.Gif, MediaFormat.Mp4, ToMp4 with { Mode = CompressionMode.Lossless }));
        Assert.False(engine.CanHandle(MediaFormat.Gif, MediaFormat.Gif, ToMp4));
        Assert.False(engine.CanHandle(MediaFormat.Mov, MediaFormat.Mp4, ToMp4));
    }

    [Fact]
    public void Transparency_is_read_from_the_analysis()
    {
        const string opaque = "frame:0 pts:0 pts_time:0\nlavfi.signalstats.YMIN=255\nframe:1 pts:10 pts_time:0.1\nlavfi.signalstats.YMIN=255\n";
        Assert.False(FfmpegGifVideoCompressor.HasTransparency(opaque));
        Assert.True(FfmpegGifVideoCompressor.HasTransparency(opaque + "frame:2 pts:20 pts_time:0.2\r\nlavfi.signalstats.YMIN=0\r\n"));
        Assert.False(FfmpegGifVideoCompressor.HasTransparency(""));
    }

    [Fact]
    public void The_command_line_follows_the_settings()
    {
        var mp4 = FfmpegGifVideoCompressor.Arguments(new CompressionJob("in.gif", MediaFormat.Gif, "out.mp4", MediaFormat.Mp4, ToMp4));
        var webm = FfmpegGifVideoCompressor.Arguments(
            new CompressionJob("in.gif", MediaFormat.Gif, "out.webm", MediaFormat.WebM, ToMp4 with { Effort = CompressionEffort.Fast })
            {
                ResizeScale = 0.5,
            });

        Assert.Equal(["-c:v", "libx264", "-preset", "slow", "-crf", "23"], mp4.Skip(mp4.IndexOf("-c:v")).Take(6));
        Assert.Equal("cfr", mp4[mp4.IndexOf("-fps_mode") + 1]);
        Assert.Contains("+faststart", mp4);
        Assert.Equal(["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "34", "-deadline", "good", "-cpu-used", "4"],
            webm.Skip(webm.IndexOf("-c:v")).Take(10));
        Assert.Equal("vfr", webm[webm.IndexOf("-fps_mode") + 1]);
        Assert.Contains("iw*0.5", webm[webm.IndexOf("-vf") + 1]);
        Assert.DoesNotContain("+faststart", webm);
    }

    [Fact]
    public async Task An_animated_gif_becomes_a_smaller_mp4_of_the_same_length()
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        // Odd size (H.264 needs even), and a 2 s pause at the end merged into one long frame by gifsicle.
        var gif = await WriteGifAsync(ffmpeg, dir.File("anim.gif"), "testsrc2=s=161x121:d=1:r=10,tpad=stop_mode=clone:stop_duration=2");
        await MergeIdenticalFramesAsync(gif);

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(gif, ToMp4, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal("ffmpeg", result.CompressorId);
        Assert.Equal(dir.File("anim-compressed.mp4"), result.OutputPath);
        Assert.True(result.FinalSize < result.OriginalSize, $"{result.FinalSize} / {result.OriginalSize}");
        var video = await DescribeAsync(ffmpeg, result.OutputPath!);
        Assert.Equal(("h264", 160, 120), (video.Codec, video.Width, video.Height));
        Assert.InRange(video.Duration.TotalSeconds, 2.9, 3.1);
        Assert.True(File.Exists(gif)); // kept alongside
    }

    [Fact]
    public async Task It_can_be_a_webm_scaled_down_with_the_image_size_limit()
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var gif = await WriteGifAsync(ffmpeg, dir.File("anim.gif"), "testsrc2=s=320x240:d=1:r=10");

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(
            gif, ToMp4 with { AnimatedGifs = AnimatedGifOutput.WebM, ImageMaxSide = 160 }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.EndsWith(".webm", result.OutputPath);
        var video = await DescribeAsync(ffmpeg, result.OutputPath!);
        Assert.Equal(("vp9", 160, 120), (video.Codec, video.Width, video.Height));
    }

    [Fact]
    public async Task A_transparent_gif_stays_a_gif_and_says_why()
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var gif = await WriteGifAsync(ffmpeg, dir.File("sticker.gif"),
            "color=c=red@0.0:s=120x80:d=1:r=10,format=rgba,drawbox=x=t*80:y=20:w=30:h=30:c=blue@1:t=fill",
            "palettegen=reserve_transparent=1", "paletteuse=alpha_threshold=128");

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(gif, ToMp4, Ct);

        Assert.NotEqual(CompressionStatus.Converted, result.Status);
        Assert.Equal("gifsicle", result.CompressorId);
        Assert.Contains("transparent", result.Message);
        Assert.False(File.Exists(dir.File("sticker-compressed.mp4")));
    }

    [Theory]
    [InlineData(false)] // a still GIF: nothing moves, nothing to gain from a video
    [InlineData(true)]  // lossless mode never turns a GIF into a video
    public async Task Still_gifs_and_lossless_mode_keep_the_gif(bool lossless)
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var gif = await WriteGifAsync(ffmpeg, dir.File("image.gif"), lossless ? "testsrc2=s=160x120:d=1:r=10" : "testsrc2=s=160x120:d=0.1:r=10");

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(
            gif, lossless ? ToMp4 with { Mode = CompressionMode.Lossless } : ToMp4, Ct);

        Assert.Equal("gifsicle", result.CompressorId);
        Assert.Null(result.Message);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.mp4"));
    }

    /// <summary>Writes a GIF from an ffmpeg test source, with a palette made for it.</summary>
    private static async Task<string> WriteGifAsync(
        string ffmpeg, string path, string source, string palette = "palettegen", string use = "paletteuse")
    {
        var result = await ExternalTool.RunRawAsync(ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", source, "-vf", $"split[a][b];[a]{palette}[p];[b][p]{use}", path], Ct);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return path;
    }

    /// <summary>gifsicle turns identical frames in a row into one longer frame, as optimized GIFs often have.</summary>
    private static async Task MergeIdenticalFramesAsync(string path)
    {
        var gifsicle = RequireTool("gifsicle.exe");
        await ExternalTool.RunAsync(gifsicle, ["-O2", "--batch", path], Ct);
    }

    /// <summary>The first video stream's codec and size, and the duration, as ffmpeg sees the file.</summary>
    private static async Task<(string Codec, int Width, int Height, TimeSpan Duration)> DescribeAsync(string ffmpeg, string path)
    {
        var description = (await ExternalTool.RunRawAsync(ffmpeg, ["-hide_banner", "-i", path], Ct)).StandardError;
        var match = Regex.Match(description, @"Video: (\w+).*?, (\d{2,5})x(\d{2,5})");
        Assert.True(match.Success, description);
        return (match.Groups[1].Value, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
            FfmpegVideoCompressor.ParseProbe(description).Duration);
    }
}
