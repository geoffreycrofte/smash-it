using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;
using static SmashIt.Core.Compressors.FfmpegAudioCompressor;

namespace SmashIt.Core.Tests;

/// <summary>Sound files through ffmpeg: lossless FLAC, lossy MP3, AAC, Vorbis and Opus.</summary>
public class FfmpegAudioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions Lossy = new()
    {
        Mode = CompressionMode.Lossy, Quality = 70, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    private static readonly CompressionOptions Lossless = Lossy with { Mode = CompressionMode.Lossless };

    private static string RequireFfmpeg()
    {
        var path = ExternalTool.Find("ffmpeg");
        if (path is null) Assert.Skip("ffmpeg absent : lancez tools\\fetch-tools.ps1.");
        return path;
    }

    private static CompressionJob Job(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        new("in" + MediaFormatInfo.ExtensionOf(input), input, "out" + MediaFormatInfo.ExtensionOf(output), output, options);

    [Theory]
    [InlineData(MediaFormat.Wav, null, MediaFormat.Flac)]
    [InlineData(MediaFormat.Aiff, null, MediaFormat.Flac)]
    [InlineData(MediaFormat.Aac, null, MediaFormat.M4a)]
    [InlineData(MediaFormat.Mp3, null, MediaFormat.Mp3)]
    [InlineData(MediaFormat.Mp3, MediaFormat.Opus, MediaFormat.Opus)]
    [InlineData(MediaFormat.Wav, MediaFormat.Aac, MediaFormat.M4a)]
    [InlineData(MediaFormat.Flac, MediaFormat.Jpeg, MediaFormat.Flac)] // an image target doesn't apply to sound
    public void Sound_formats_resolve_to_a_writable_output(MediaFormat input, MediaFormat? target, MediaFormat expected) =>
        Assert.Equal(expected, MediaFormatInfo.ResolveOutput(input, target));

    [Fact]
    public void Lossless_mode_never_reencodes_a_lossy_file_and_flac_needs_lossless_sound()
    {
        var engine = new FfmpegAudioCompressor();
        Assert.True(engine.CanHandle(MediaFormat.Wav, MediaFormat.Flac, Lossless));
        Assert.True(engine.CanHandle(MediaFormat.Mp3, MediaFormat.Mp3, Lossless));     // tags removed, sound copied
        Assert.False(engine.CanHandle(MediaFormat.Mp3, MediaFormat.Opus, Lossless));
        Assert.False(engine.CanHandle(MediaFormat.Mp3, MediaFormat.Flac, Lossy));      // would only get larger
        Assert.True(engine.CanHandle(MediaFormat.Flac, MediaFormat.Opus, Lossy));
        Assert.False(engine.CanHandle(MediaFormat.Mp4, MediaFormat.Mp3, Lossy));       // a video isn't a sound file
        Assert.False(engine.CanHandle(MediaFormat.Mp3, MediaFormat.Wav, Lossy));       // WAV is never written
    }

    [Theory]
    [InlineData(MediaFormat.Opus, AudioContent.Music, 100, 160)]
    [InlineData(MediaFormat.Opus, AudioContent.Music, 70, 96)]
    [InlineData(MediaFormat.Opus, AudioContent.Music, 40, 64)]
    [InlineData(MediaFormat.Opus, AudioContent.Voice, 70, 32)]
    [InlineData(MediaFormat.M4a, AudioContent.Music, 70, 160)]
    [InlineData(MediaFormat.M4a, AudioContent.Voice, 70, 64)]
    [InlineData(MediaFormat.Mp3, AudioContent.Music, 70, 190)] // LAME V2
    [InlineData(MediaFormat.Mp3, AudioContent.Voice, 40, 48)]
    public void Quality_and_content_set_the_bitrate(MediaFormat output, AudioContent content, int quality, int kbps) =>
        Assert.Equal(kbps, TargetKbps(output, quality, content));

    [Fact]
    public void The_plan_follows_the_mode_the_content_and_the_source()
    {
        var stereoWav = new SourceInfo(TimeSpan.FromSeconds(60), "pcm_s16le", 2, 1_411_000);
        var mp3At128 = new SourceInfo(TimeSpan.FromSeconds(60), "mp3", 1, 128_000);
        var mp3At64 = mp3At128 with { Bitrate = 64_000 };

        Assert.Equal(new Plan(Method.Flac), Decide(Job(MediaFormat.Wav, MediaFormat.Flac, Lossy), stereoWav));
        Assert.Equal(new Plan(Method.Copy), Decide(Job(MediaFormat.Mp3, MediaFormat.Mp3, Lossless), mp3At128));
        Assert.Equal(new Plan(Method.Encode, 96_000), Decide(Job(MediaFormat.Wav, MediaFormat.Opus, Lossy), stereoWav));
        Assert.Equal(new Plan(Method.Encode, 0, 2, Mono: false), Decide(Job(MediaFormat.Wav, MediaFormat.Mp3, Lossy), stereoWav));
        Assert.Equal(new Plan(Method.Encode, 32_000, Mono: true),
            Decide(Job(MediaFormat.Wav, MediaFormat.Opus, Lossy with { AudioContent = AudioContent.Voice }), stereoWav));
        // A mono MP3 at 128 kbit/s is already lighter than mono music needs: copied, not re-encoded.
        Assert.Equal(new Plan(Method.Copy), Decide(Job(MediaFormat.Mp3, MediaFormat.Mp3, Lossy), mp3At128));
        // As voice, it is worth re-encoding at 64 kbit/s.
        Assert.Equal(new Plan(Method.Encode, 64_000, Mono: true),
            Decide(Job(MediaFormat.Mp3, MediaFormat.Mp3, Lossy with { AudioContent = AudioContent.Voice }), mp3At128));
        // Converted to Opus, never more bits than the source had.
        Assert.Equal(new Plan(Method.Encode, 57_000, Mono: true), Decide(Job(MediaFormat.Mp3, MediaFormat.Opus, Lossy), mp3At64));
    }

    [Fact]
    public void The_probe_reads_codec_channels_and_bitrate()
    {
        static SourceInfo? Probe(string stream, string bitrate = "1411") => ParseProbe(
            $"Input #0, x, from 'in':\n  Duration: 00:01:00.00, start: 0.000000, bitrate: {bitrate} kb/s\n  Stream #0:0: {stream}\n");

        Assert.Equal(new SourceInfo(TimeSpan.FromMinutes(1), "mp3", 1, 128_000), Probe("Audio: mp3 (mp3float), 44100 Hz, mono, fltp, 128 kb/s"));
        Assert.Equal(new SourceInfo(TimeSpan.FromMinutes(1), "flac", 2, 900_000), Probe("Audio: flac, 44100 Hz, stereo, s16", "900"));
        Assert.Equal(6, Probe("Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, 5.1, fltp, 384 kb/s")!.Channels);
        Assert.Equal(6, Probe("Audio: pcm_s16le ([1][0][0][0] / 0x0001), 44100 Hz, 6 channels, s16, 4233 kb/s")!.Channels);
        Assert.Null(Probe("Video: h264, yuv420p, 1920x1080"));
    }

    [Fact]
    public async Task A_wav_becomes_a_smaller_flac_with_every_sample_kept()
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var wav = await WriteSoundAsync(ffmpeg, dir.File("tone.wav"), "pcm_s16le");

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(wav, Lossless, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(dir.File("tone-compressed.flac"), result.OutputPath);
        Assert.True(result.FinalSize < result.OriginalSize);
        Assert.Equal(await SamplesHashAsync(ffmpeg, wav), await SamplesHashAsync(ffmpeg, result.OutputPath!));
    }

    [Theory]
    [InlineData(AudioContent.Music, 2)]
    [InlineData(AudioContent.Voice, 1)]
    public async Task Lossy_sound_becomes_opus_in_stereo_or_mono(AudioContent content, int channels)
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var wav = await WriteSoundAsync(ffmpeg, dir.File("song.wav"), "pcm_s16le");

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(
            wav, Lossy with { TargetFormat = MediaFormat.Opus, AudioContent = content }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.EndsWith(".opus", result.OutputPath);
        var info = ParseProbe((await ExternalTool.RunRawAsync(ffmpeg, ["-hide_banner", "-i", result.OutputPath!], Ct)).StandardError)!;
        Assert.Equal(("opus", channels), (info.Codec, info.Channels));
        Assert.InRange(info.Duration.TotalSeconds, 2.9, 3.1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tags_and_cover_art_follow_the_metadata_setting(bool strip)
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var mp3 = dir.File("chapter.mp3");
        var made = await ExternalTool.RunRawAsync(ffmpeg,
        [
            "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-f", "lavfi", "-i", "color=c=red:s=64x64:d=1",
            "-map", "0:a", "-map", "1:v", "-frames:v", "1", "-c:a", "libmp3lame", "-b:a", "192k", "-c:v", "mjpeg",
            "-disposition:v", "attached_pic", "-id3v2_version", "3", "-metadata", "title=Chapter 1", mp3,
        ], Ct);
        Assert.True(made.ExitCode == 0, made.StandardError);

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(
            mp3, Lossy with { AudioContent = AudioContent.Voice, StripMetadata = strip }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        var description = (await ExternalTool.RunRawAsync(ffmpeg, ["-hide_banner", "-i", result.OutputPath!], Ct)).StandardError;
        Assert.Equal(!strip, description.Contains("Chapter 1", StringComparison.Ordinal));
        Assert.Equal(!strip, description.Contains("attached pic", StringComparison.Ordinal));
        Assert.Equal(1, ParseProbe(description)!.Channels);
    }

    [Fact]
    public async Task Floating_point_samples_are_not_rounded_into_a_flac()
    {
        var ffmpeg = RequireFfmpeg();
        using var dir = new TempDirectory();
        var wav = await WriteSoundAsync(ffmpeg, dir.File("float.wav"), "pcm_f32le");

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(wav, Lossless, Ct);

        Assert.Equal(CompressionStatus.Unsupported, result.Status);
        Assert.Contains("floating-point", result.Message);
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    /// <summary>3 seconds of stereo sound: a tone on the left, noise on the right.</summary>
    private static async Task<string> WriteSoundAsync(string ffmpeg, string path, string codec)
    {
        var result = await ExternalTool.RunRawAsync(ffmpeg,
        [
            "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-f", "lavfi", "-i", "anoisesrc=d=3:c=pink:a=0.1", "-filter_complex", "[0][1]join=inputs=2:channel_layout=stereo",
            "-c:a", codec, path,
        ], Ct);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return path;
    }

    /// <summary>The MD5 of the decoded samples, to compare the sound of two files whatever their format.</summary>
    private static async Task<string> SamplesHashAsync(string ffmpeg, string path)
    {
        var result = await ExternalTool.RunRawAsync(ffmpeg,
            ["-hide_banner", "-nostdin", "-i", path, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "md5", "-"], Ct);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return result.StandardOutput.Trim();
    }
}
