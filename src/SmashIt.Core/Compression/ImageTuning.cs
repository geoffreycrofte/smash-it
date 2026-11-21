namespace SmashIt.Core.Compression;

/// <summary>How much color detail lossy JPEG and AVIF images keep (chroma subsampling).</summary>
public enum ColorDetail
{
    /// <summary>JPEG: reduced below quality 90, full above. AVIF: full (avifenc's default).</summary>
    Automatic,

    /// <summary>4:4:4: sharp colored edges (text, logos, screenshots), larger files.</summary>
    Full,

    /// <summary>4:2:0: color at half resolution, invisible on photos, smaller files.</summary>
    Reduced,
}

/// <summary>How jpegtran writes JPEG files.</summary>
public enum JpegEncoding
{
    /// <summary>Progressive and baseline are both tried, the smaller one wins (progressive only at Fast effort).</summary>
    Automatic,

    /// <summary>Shows a blurry preview first while loading on the web; usually smaller.</summary>
    Progressive,

    /// <summary>Loads top to bottom; for old devices and software that can't read progressive JPEGs.</summary>
    Baseline,
}

/// <summary>
/// Per-engine settings for the image engines, on top of quality and effort. The defaults keep Smash It's usual
/// behavior; presets don't change them.
/// </summary>
public sealed record ImageTuning
{
    /// <summary>Lossy JPEG (Windows encoder) and AVIF (avifenc).</summary>
    public ColorDetail ColorDetail { get; init; }

    /// <summary>jpegtran, lossless and after a lossy JPEG encode.</summary>
    public JpegEncoding JpegEncoding { get; init; }

    /// <summary>
    /// Lossy PNG (pngquant) and GIF (gifsicle): palette size at most, 2–256. 0 lets the engines decide (up to 256).
    /// A color count chosen here is applied even when it costs more quality than the usual limit.
    /// </summary>
    public int MaxColors { get; init; }

    /// <summary>Lossy PNG: pngquant's dithering, which smooths gradients; off, flat images get much smaller.</summary>
    public bool Dithering { get; init; } = true;

    /// <summary>PNG (oxipng): the Zopfli compressor, a few percent smaller and dozens of times slower.</summary>
    public bool Zopfli { get; init; }

    public static ImageTuning Default { get; } = new();
}
