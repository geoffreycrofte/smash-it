using System.Buffers.Binary;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SmashIt.Core.Tests;

/// <summary>Builds real MP4 files with Windows' own encoder and inspects them.</summary>
internal static class TestVideo
{
    /// <summary>An MP4 showing a still frame: left half red, right half blue.</summary>
    public static async Task<string> WriteAsync(string path, uint width = 640, uint height = 360, double seconds = 2, uint bitrate = 6_000_000)
    {
        var frame = path + ".frame.png";
        var pixels = new byte[width * height * 4];
        var random = new Random(1);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (int)((y * width + x) * 4);
            var noise = (byte)random.Next(40);
            var left = x < width / 2;
            pixels[i] = left ? noise : (byte)(215 + noise);     // B
            pixels[i + 1] = noise;                              // G
            pixels[i + 2] = left ? (byte)(215 + noise) : noise; // R
            pixels[i + 3] = 255;
        }
        await using (var stream = new FileStream(frame, FileMode.Create, FileAccess.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
            await encoder.FlushAsync();
        }

        var clip = await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(frame), TimeSpan.FromSeconds(seconds));
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = width;
        profile.Video.Height = height;
        profile.Video.Bitrate = bitrate;
        profile.Audio = null;

        var result = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile);
        File.Delete(frame);
        if (result != TranscodeFailureReason.None) throw new InvalidOperationException($"Rendu de la vidéo de test impossible : {result}");
        return path;
    }

    /// <summary>Sets a 90° clockwise rotation in the video track header, like a phone filming in portrait.</summary>
    public static void RotateClockwise90(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var span = bytes.AsSpan();
        for (var i = 4; i + 4 <= bytes.Length; i++)
        {
            if (!span.Slice(i, 4).SequenceEqual("tkhd"u8)) continue;
            var version = bytes[i + 4];
            var matrix = i + 4 + (version == 1 ? 52 : 40);
            var width = BinaryPrimitives.ReadUInt32BigEndian(span[(matrix + 36)..]) >> 16;
            var height = BinaryPrimitives.ReadUInt32BigEndian(span[(matrix + 40)..]) >> 16;
            if (width == 0) continue; // audio track

            int[] values = [0, 0x00010000, 0, unchecked((int)0xFFFF0000), 0, 0, (int)(height << 16), 0, 0x40000000];
            for (var k = 0; k < 9; k++) BinaryPrimitives.WriteInt32BigEndian(span[(matrix + k * 4)..], values[k]);
            _ = width;
            File.WriteAllBytes(path, bytes);
            return;
        }
        throw new InvalidDataException("Piste vidéo introuvable.");
    }

    public static async Task<VideoProperties> PropertiesAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        return await file.Properties.GetVideoPropertiesAsync();
    }

    /// <summary>Average red and blue of the top and bottom halves of the frame at 1 s.</summary>
    public static async Task<(double TopRed, double TopBlue, double BottomRed, double BottomBlue, uint Width, uint Height)> SampleFrameAsync(string path)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        using var image = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(1), 0, 0, VideoFramePrecision.NearestFrame);
        var decoder = await BitmapDecoder.CreateAsync(image);
        var data = (await decoder.GetPixelDataAsync()).DetachPixelData();
        uint w = decoder.PixelWidth, h = decoder.PixelHeight;

        double tr = 0, tb = 0, br = 0, bb = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (int)((y * w + x) * 4);
            if (y < h / 2) { tb += data[i]; tr += data[i + 2]; }
            else { bb += data[i]; br += data[i + 2]; }
        }
        var half = (double)w * h / 2;
        return (tr / half, tb / half, br / half, bb / half, w, h);
    }
}
