using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Tests;

public class OxvgCompressorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An SVG as an editor exports it: comments, metadata, editor data, an ID, verbose numbers.</summary>
    private const string EditorSvg = """
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <!-- Generator: Adobe Illustrator 27.0.0, SVG Export Plug-In -->
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape"
             xmlns:sodipodi="http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd"
             width="240.000px" height="120.000px" viewBox="0 0 240 120" version="1.1">
          <title>Smash It logo</title>
          <metadata><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description about="test"/></rdf:RDF></metadata>
          <sodipodi:namedview id="namedview1" pagecolor="#ffffff" inkscape:zoom="1.5"/>
          <g id="layer1" inkscape:label="Layer 1" inkscape:groupmode="layer">
            <rect id="background" x="0.000000" y="0.000000" width="240.000000" height="120.000000" fill="#FF0000" fill-opacity="1.0000"/>
            <circle id="dot" cx="60.0000001" cy="60.000000" r="40.000000" style="fill:#ffffff;stroke:none"/>
            <path id="arrow" d="M 120.000000,20.000000 L 220.000000,60.000000 L 120.000000,100.000000 Z" fill="#000000"/>
          </g>
        </svg>
        """;

    [Fact]
    public void The_configuration_keeps_ids_and_follows_the_metadata_setting()
    {
        Assert.Equal("""{"optimise":{"extends":"default","jobs":{},"omit":["cleanup_ids"]}}""",
            OxvgCompressor.Configuration(CompressionOptions.Default));
        Assert.Equal(
            """{"optimise":{"extends":"default","jobs":{},"omit":["cleanup_ids","remove_comments","remove_metadata","remove_editors_n_s_data"]}}""",
            OxvgCompressor.Configuration(CompressionOptions.Default with { StripMetadata = false }));
    }

    [Theory]
    [InlineData("100", "100.000", true)]
    [InlineData("100px", "100.0001px", true)]
    [InlineData("0 0 24 24", "0,0,24,24", true)]
    [InlineData("100%", "100%", true)]
    [InlineData("100px", "100", true)]
    [InlineData("100mm", "100", false)]
    [InlineData("100", "50", false)]
    [InlineData("0 0 24 24", "0 0 24", false)]
    [InlineData("", "", true)]
    [InlineData("100", "", false)]
    public void Compares_sizes_by_value(string a, string b, bool same) =>
        Assert.Equal(same, OxvgCompressor.SameValue(a, b));

    [Fact]
    public void Sets_aside_a_result_that_is_broken_or_changes_size()
    {
        using var dir = new TempDirectory();
        var input = dir.File("in.svg");
        File.WriteAllText(input, EditorSvg);

        File.WriteAllText(dir.File("ok.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="240px" height="120px" viewBox="0,0,240,120"><path d="M0 0h240v120H0z"/></svg>""");
        File.WriteAllText(dir.File("broken.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="240px"><path""");
        File.WriteAllText(dir.File("no-viewbox.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="240px" height="120px"><path d="M0 0h240v120H0z"/></svg>""");
        File.WriteAllText(dir.File("not-svg.svg"), """<html/>""");

        Assert.Null(OxvgCompressor.CheckSameDocument(input, dir.File("ok.svg")));
        Assert.NotNull(OxvgCompressor.CheckSameDocument(input, dir.File("broken.svg")));
        Assert.Contains("viewBox", OxvgCompressor.CheckSameDocument(input, dir.File("no-viewbox.svg")));
        Assert.NotNull(OxvgCompressor.CheckSameDocument(input, dir.File("not-svg.svg")));
    }

    private const string SvgFont = """
        <svg xmlns="http://www.w3.org/2000/svg"><defs><font id="icons" horiz-adv-x="1024">
          <font-face units-per-em="1024" ascent="960" descent="-64"/>
          <glyph unicode="&#xe600;" glyph-name="mail" d="M0 768v-640h896v640h-896z"/>
        </font></defs></svg>
        """;

    [Fact]
    public void Recognizes_svg_fonts()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("font.svg"), SvgFont);
        File.WriteAllText(dir.File("logo.svg"), EditorSvg);

        Assert.True(OxvgCompressor.IsSvgFont(dir.File("font.svg")));
        Assert.False(OxvgCompressor.IsSvgFont(dir.File("logo.svg")));
    }

    [Fact]
    public void Sets_aside_a_result_with_nothing_left_to_draw()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("in.svg"), EditorSvg);
        File.WriteAllText(dir.File("empty.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="240" height="120" viewBox="0 0 240 120"><title>Smash It logo</title></svg>""");

        Assert.NotNull(OxvgCompressor.CheckSameDocument(dir.File("in.svg"), dir.File("empty.svg")));
        Assert.Null(OxvgCompressor.CheckSameDocument(dir.File("in.svg"), dir.File("in.svg")));
    }

    private static OxvgCompressor RequireOxvg()
    {
        var oxvg = new OxvgCompressor();
        if (!oxvg.IsAvailable) Assert.Skip("oxvg absent : lancez tools\\fetch-tools.ps1.");
        return oxvg;
    }

    [Fact]
    public async Task Cleans_an_editor_export_and_keeps_what_matters()
    {
        var oxvg = RequireOxvg();
        using var dir = new TempDirectory();
        var path = dir.File("logo.svg");
        await File.WriteAllTextAsync(path, EditorSvg, Ct);
        var before = new FileInfo(path).Length;

        var result = await new CompressionPipeline([oxvg])
            .ProcessAsync(path, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        var svg = await File.ReadAllTextAsync(path, Ct);
        Assert.True(new FileInfo(path).Length < before * 0.7, $"{before} → {new FileInfo(path).Length}: {svg}");
        Assert.DoesNotContain("Illustrator", svg);   // comment
        Assert.DoesNotContain("rdf:RDF", svg);       // metadata
        Assert.DoesNotContain("inkscape", svg);      // editor data
        Assert.Contains("<title>Smash It logo</title>", svg); // read by screen readers
        Assert.Contains("viewBox", svg);
        Assert.Contains("id=\"arrow\"", svg);        // IDs may be used from outside
        Assert.Equal(MediaFormat.Svg, FormatDetector.DetectFile(path));
    }

    [Fact]
    public async Task Keeps_comments_and_metadata_when_asked()
    {
        var oxvg = RequireOxvg();
        using var dir = new TempDirectory();
        var path = dir.File("logo.svg");
        await File.WriteAllTextAsync(path, EditorSvg, Ct);

        var result = await new CompressionPipeline([oxvg]).ProcessAsync(path, new CompressionOptions
        {
            StripMetadata = false, OriginalHandling = OriginalHandling.Overwrite,
        }, Ct);

        Assert.True(result.IsSuccess);
        var svg = await File.ReadAllTextAsync(path, Ct);
        Assert.Contains("Illustrator", svg);
        Assert.Contains("rdf:RDF", svg);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task Leaves_svg_fonts_untouched()
    {
        var oxvg = RequireOxvg();
        using var dir = new TempDirectory();
        var path = dir.File("icons.svg");
        await File.WriteAllTextAsync(path, SvgFont, Ct);

        var result = await new CompressionPipeline([oxvg])
            .ProcessAsync(path, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.NotEqual(CompressionStatus.Optimized, result.Status);
        Assert.Equal(SvgFont, await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task Leaves_a_file_it_cannot_read_untouched()
    {
        var oxvg = RequireOxvg();
        using var dir = new TempDirectory();
        var path = dir.File("broken.svg");
        const string broken = """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" """;
        await File.WriteAllTextAsync(path, broken, Ct);

        var result = await new CompressionPipeline([oxvg])
            .ProcessAsync(path, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.NotEqual(CompressionStatus.Optimized, result.Status);
        Assert.Equal(broken, await File.ReadAllTextAsync(path, Ct));
    }
}
