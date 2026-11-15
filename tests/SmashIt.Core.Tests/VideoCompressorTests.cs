using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Tests;

/// <summary>Uses Windows' own video encoder (Media Foundation).</summary>
public class VideoCompressorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions Lossy = new()
    {
        Mode = CompressionMode.Lossy, Quality = 30, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    [Theory]
    [InlineData(MediaFormat.Mov, null, MediaFormat.Mp4)]
    [InlineData(MediaFormat.Mkv, null, MediaFormat.Mp4)]
    [InlineData(MediaFormat.Mp4, MediaFormat.Jpeg, MediaFormat.Mp4)]
    [InlineData(MediaFormat.Pdf, MediaFormat.Jpeg, MediaFormat.Pdf)]
    [InlineData(MediaFormat.Png, MediaFormat.WebP, MediaFormat.WebP)]
    [InlineData(MediaFormat.Png, null, MediaFormat.Png)]
    public void Output_format_follows_the_kind_of_file(MediaFormat input, MediaFormat? target, MediaFormat expected) =>
        Assert.Equal(expected, MediaFormatInfo.ResolveOutput(input, target));

    [Fact]
    public void Bitrate_grows_with_quality_and_never_exceeds_the_source()
    {
        var low = VideoCompressor.TargetVideoBitrate(30, VideoCodec.H264, 1920, 1080, 30, 0);
        var high = VideoCompressor.TargetVideoBitrate(85, VideoCodec.H264, 1920, 1080, 30, 0);
        var hevc = VideoCompressor.TargetVideoBitrate(85, VideoCodec.Hevc, 1920, 1080, 30, 0);

        Assert.True(low < high);
        Assert.True(hevc < high);
        Assert.InRange(high, 6_000_000u, 8_000_000u);
        Assert.True(VideoCompressor.TargetVideoBitrate(100, VideoCodec.H264, 1920, 1080, 30, 2_000_000) <= 1_900_000);
    }

    [Fact]
    public void Source_bitrate_falls_back_to_size_over_duration()
    {
        Assert.Equal(5_000_000u, VideoCompressor.SourceVideoBitrate(5_000_000, 9_000_000, 0, TimeSpan.Zero, 128_000));
        Assert.Equal(8_872_000u, VideoCompressor.SourceVideoBitrate(0, 9_000_000, 0, TimeSpan.Zero, 128_000));
        // 10 MB over 10 s = 8 Mbit/s total.
        Assert.Equal(7_872_000u, VideoCompressor.SourceVideoBitrate(0, 0, 10_000_000, TimeSpan.FromSeconds(10), 128_000));
        Assert.Equal(0u, VideoCompressor.SourceVideoBitrate(0, 0, 10_000_000, TimeSpan.Zero, 128_000));
    }

    [Fact]
    public async Task Lossless_mode_explains_that_videos_need_lossy()
    {
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("clip.mp4"));

        var result = await new CompressionPipeline([new VideoCompressor()])
            .ProcessAsync(video, new CompressionOptions(), Ct);

        Assert.Equal(CompressionStatus.Unsupported, result.Status);
        Assert.Contains("Avec pertes", result.Message);
    }

    [Fact]
    public async Task Compresses_an_h264_video_and_reports_progress()
    {
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("clip.mp4"), bitrate: 8_000_000);
        var reported = new List<double>();
        var progress = new SynchronousProgress(reported.Add);

        var result = await new CompressionPipeline([new VideoCompressor()]).ProcessAsync(video, Lossy, Ct, progress);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal(dir.File("clip-compressed.mp4"), result.OutputPath);
        Assert.True(result.FinalSize < result.OriginalSize);
        var properties = await TestVideo.PropertiesAsync(result.OutputPath!);
        Assert.Equal((640u, 360u), (properties.Width, properties.Height));
        Assert.InRange(properties.Duration.TotalSeconds, 1.8, 2.2);
        Assert.NotEmpty(reported);
    }

    [Fact]
    public async Task Hevc_output_is_smaller_than_h264()
    {
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("clip.mp4"), bitrate: 8_000_000);
        var compressor = new VideoCompressor();
        var h264 = dir.File("h264.mp4");
        var hevc = dir.File("hevc.mp4");

        await compressor.CompressAsync(new CompressionJob(video, MediaFormat.Mp4, h264, MediaFormat.Mp4, Lossy with { Quality = 70 }), Ct);
        try
        {
            await compressor.CompressAsync(new CompressionJob(video, MediaFormat.Mp4, hevc, MediaFormat.Mp4,
                Lossy with { Quality = 70, VideoCodec = VideoCodec.Hevc }), Ct);
        }
        catch (NotSupportedException ex)
        {
            Assert.Skip($"Pas d'encodeur HEVC sur cette machine : {ex.Message}");
        }

        Assert.True(new FileInfo(hevc).Length < new FileInfo(h264).Length);
    }

    [Fact]
    public async Task Portrait_phone_videos_are_turned_upright()
    {
        using var dir = new TempDirectory();
        var video = await TestVideo.WriteAsync(dir.File("portrait.mp4"), bitrate: 8_000_000);
        TestVideo.RotateClockwise90(video);
        Assert.Equal(Windows.Storage.FileProperties.VideoOrientation.Rotate90, (await TestVideo.PropertiesAsync(video)).Orientation);

        var result = await new CompressionPipeline([new VideoCompressor()]).ProcessAsync(video, Lossy with { Quality = 60 }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        var properties = await TestVideo.PropertiesAsync(result.OutputPath!);
        Assert.Equal((360u, 640u), (properties.Width, properties.Height));
        // Rotating the frame clockwise moves its left (red) half to the top.
        var frame = await TestVideo.SampleFrameAsync(result.OutputPath!);
        Assert.Equal((360u, 640u), (frame.Width, frame.Height));
        Assert.True(frame.TopRed > frame.TopBlue + 100, $"haut : R={frame.TopRed:0} B={frame.TopBlue:0}");
        Assert.True(frame.BottomBlue > frame.BottomRed + 100, $"bas : R={frame.BottomRed:0} B={frame.BottomBlue:0}");
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
