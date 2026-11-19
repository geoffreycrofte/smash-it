using SmashIt.Core.Compression;

namespace SmashIt.Core.Tests;

public class CompressionPresetTests
{
    [Fact]
    public void Every_preset_is_recognized_from_its_own_settings()
    {
        foreach (var (id, settings) in CompressionPresets.All)
            Assert.Equal(id, CompressionPresets.Match(settings));
    }

    [Fact]
    public void Presets_are_all_different()
    {
        var all = CompressionPresets.All.Values.ToList();
        Assert.Equal(all.Count, all.Select(CompressionPresets.Match).Distinct().Count());
    }

    [Fact]
    public void The_default_settings_are_the_lossless_preset()
    {
        // AppSettings' defaults: lossless images and PDFs, metadata removed, video at 70.
        var defaults = new PresetSettings(false, 85, true, false, 75, 150, 70);

        Assert.Equal(PresetId.Lossless, CompressionPresets.Match(defaults));
    }

    [Fact]
    public void Settings_without_effect_are_ignored()
    {
        // Lossless: the quality sliders and the PDF resolution don't matter.
        var lossless = CompressionPresets.All[PresetId.Lossless] with { ImageQuality = 40, PdfQuality = 10, PdfMaxImageDpi = 0 };

        Assert.Equal(PresetId.Lossless, CompressionPresets.Match(lossless));
    }

    [Fact]
    public void Changed_settings_are_custom()
    {
        Assert.Null(CompressionPresets.Match(CompressionPresets.All[PresetId.Web] with { ImageQuality = 81 }));
        Assert.Null(CompressionPresets.Match(CompressionPresets.All[PresetId.Web] with { PdfMaxImageDpi = 300 }));
        Assert.Null(CompressionPresets.Match(CompressionPresets.All[PresetId.Archive] with { StripMetadata = true, VideoQuality = 50 }));
    }
}
