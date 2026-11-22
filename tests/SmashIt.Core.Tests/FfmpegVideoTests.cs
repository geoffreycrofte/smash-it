using System.Text.RegularExpressions;
using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>AV1 videos through ffmpeg (SVT-AV1).</summary>
public class FfmpegVideoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions Av1 = new()
    {
        Mode = CompressionMode.Lossy, Quality = 70, VideoCodec = VideoCodec.Av1, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    private static FfmpegVideoCompressor Require()
    {
        var ffmpeg = new FfmpegVideoCompressor();
        if (!ffmpeg.IsAvailable) Assert.Skip("ffmpeg absent : lancez tools\\fetch-tools.ps1.");
        return ffmpeg;
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(70, 34)]
    [InlineData(40, 48)]
    [InlineData(1, 63)]
    public void Quality_maps_to_a_crf(int quality, int crf) => Assert.Equal(crf, FfmpegVideoCompressor.Crf(quality));

#if WINDOWS // WIC or Media Foundation
    [Fact]
    public void Exactly_one_engine_encodes_each_video()
    {
        var mf = new VideoCompressor();
        var ffmpeg = new FfmpegVideoCompressor();
        var windows = Av1 with { VideoEncoder = VideoEncoder.Windows };
        foreach (var codec in Enum.GetValues<VideoCodec>())
        foreach (var options in new[] { Av1 with { VideoCodec = codec }, windows with { VideoCodec = codec } })
        {
            var engines = new ICompressor[] { mf, ffmpeg }.Count(e => e.CanHandle(MediaFormat.Mov, MediaFormat.Mp4, options));
            Assert.Equal(1, engines);
        }

        // ffmpeg is the default for every codec; the Windows encoder only takes H.264 and HEVC when chosen.
        Assert.True(ffmpeg.CanHandle(MediaFormat.Mov, MediaFormat.Mp4, Av1 with { VideoCodec = VideoCodec.H264 }));
        Assert.True(ffmpeg.CanHandle(MediaFormat.Mov, MediaFormat.Mp4, windows));
        Assert.True(mf.CanHandle(MediaFormat.Mov, MediaFormat.Mp4, windows with { VideoCodec = VideoCodec.Hevc }));
        Assert.False(ffmpeg.CanHandle(MediaFormat.Mov, MediaFormat.Mp4, Av1 with { Mode = CompressionMode.Lossless }));
    }
#endif

    [Theory]
    [InlineData(100, 16, 18)]
    [InlineData(70, 23, 24)]
    [InlineData(40, 30, 30)]
    public void Quality_maps_to_x264_and_x265_crf(int quality, int h264, int hevc)
    {
        Assert.Equal(h264, FfmpegVideoCompressor.H264Crf(quality));
        Assert.Equal(hevc, FfmpegVideoCompressor.HevcCrf(quality));
    }

    [Fact]
    public void H264_and_hevc_command_lines_use_x264_and_x265()
    {
        var aac = new FfmpegVideoCompressor.SourceInfo(TimeSpan.FromSeconds(10), "aac", 128_000);
        var h264 = FfmpegVideoCompressor.Arguments(
            new CompressionJob("in.mov", MediaFormat.Mov, "out.mp4", MediaFormat.Mp4, Av1 with { VideoCodec = VideoCodec.H264 }), aac);
        var hevc = FfmpegVideoCompressor.Arguments(
            new CompressionJob("in.mov", MediaFormat.Mov, "out.mp4", MediaFormat.Mp4,
                Av1 with { VideoCodec = VideoCodec.Hevc, Effort = CompressionEffort.Maximum }), aac);

        Assert.Equal(["-c:v", "libx264", "-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p"], h264.Skip(h264.IndexOf("-c:v")).Take(8));
        Assert.Equal(["-c:v", "libx265", "-preset", "slow", "-crf", "24", "-pix_fmt", "yuv420p", "-tag:v", "hvc1"],
            hevc.Skip(hevc.IndexOf("-c:v")).Take(10));
    }

#if WINDOWS // WIC or Media Foundation
    [Theory]
    [InlineData(VideoCodec.H264, "h264")]
    [InlineData(VideoCodec.Hevc, "hevc")]
    public async Task H264_and_hevc_go_through_ffmpeg_by_default(VideoCodec codec, string name)
    {
        var ffmpeg = Require();
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("clip.mp4"), 640, 360, seconds: 2);

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(video, Av1 with { VideoCodec = codec }, Ct);

        Assert.True(result.Status is CompressionStatus.Optimized or CompressionStatus.AlreadyOptimal, result.Message);
        Assert.Equal("ffmpeg", result.CompressorId);
        if (result.Status == CompressionStatus.Optimized)
            Assert.Equal((name, 640, 360), await DescribeAsync(result.OutputPath!));
    }
#endif

    [Fact]
    public void The_probe_reads_duration_and_audio()
    {
        const string description = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mov':
              Duration: 00:01:02.50, start: 0.000000, bitrate: 9512 kb/s
              Stream #0:0[0x1](und): Video: hevc (Main) (hvc1 / 0x31637668), yuv420p(tv), 1920x1080, 9300 kb/s, 30 fps
              Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 191 kb/s (default)
            """;

        var info = FfmpegVideoCompressor.ParseProbe(description);

        Assert.Equal(TimeSpan.FromSeconds(62.5), info.Duration);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal(191_000, info.AudioBitrate);
        Assert.Null(FfmpegVideoCompressor.ParseProbe("Duration: 00:00:02.00\n Stream #0:0: Video: h264").AudioCodec);
    }

    [Fact]
    public void The_command_line_follows_the_settings()
    {
        var job = new CompressionJob("in.mov", MediaFormat.Mov, "out.mp4", MediaFormat.Mp4, Av1);
        var aac = new FfmpegVideoCompressor.SourceInfo(TimeSpan.FromSeconds(10), "aac", 128_000);
        var pcm = aac with { AudioCodec = "pcm_s16le", AudioBitrate = 1_536_000 };

        var stripped = FfmpegVideoCompressor.Arguments(job, aac);
        var kept = FfmpegVideoCompressor.Arguments(job with { Options = Av1 with { StripMetadata = false, VideoMaxShortSide = 720 } }, pcm);

        Assert.Equal(["-c:v", "libsvtav1", "-preset", "7", "-crf", "34"], stripped.Skip(stripped.IndexOf("-c:v")).Take(6));
        Assert.Equal(["-c:a", "copy"], stripped.Skip(stripped.IndexOf("-c:a")).Take(2));
        Assert.Contains("-1", stripped); // -map_metadata -1
        Assert.DoesNotContain("-vf", stripped);
        Assert.Equal(["-c:a", "aac", "-b:a", "160000"], kept.Skip(kept.IndexOf("-c:a")).Take(4));
        Assert.Contains(FfmpegVideoCompressor.ScaleFilter(720), kept);
        Assert.Contains("+faststart+use_metadata_tags", kept);
    }

#if WINDOWS // WIC or Media Foundation
    [Fact]
    public async Task A_video_becomes_a_smaller_av1_mp4_with_progress()
    {
        var ffmpeg = Require();
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("clip.mp4"), 640, 360, seconds: 2);
        var reports = new List<double>();

        var result = await new CompressionPipeline([ffmpeg]).ProcessAsync(
            video, Av1, Ct, new Progress<double>(reports.Add));

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal("ffmpeg", result.CompressorId);
        var (codec, width, height) = await DescribeAsync(result.OutputPath!);
        Assert.Equal(("av1", 640, 360), (codec, width, height));
        await Task.Delay(100, Ct); // Progress<T> posts its reports
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task A_rotated_phone_video_comes_out_upright_and_scaled()
    {
        var ffmpeg = Require();
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("phone.mp4"), 1280, 720, seconds: 1);
        TestVideo.RotateClockwise90(video); // shown as 720×1280, like a portrait phone video
        var output = dir.File("out.mp4");

        await ffmpeg.CompressAsync(new CompressionJob(video, MediaFormat.Mp4, output, MediaFormat.Mp4, Av1 with { VideoMaxShortSide = 360 }), Ct);

        Assert.Equal(("av1", 360, 640), await DescribeAsync(output));
    }
#endif

    /// <summary>The first video stream's codec and size, as ffmpeg sees the file.</summary>
    private static async Task<(string Codec, int Width, int Height)> DescribeAsync(string path)
    {
        var description = (await ExternalTool.RunRawAsync(ExternalTool.Find("ffmpeg")!, ["-hide_banner", "-i", path], Ct)).StandardError;
        var match = Regex.Match(description, @"Video: (\w+).*?, (\d{2,5})x(\d{2,5})");
        Assert.True(match.Success, description);
        return (match.Groups[1].Value, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }
}
