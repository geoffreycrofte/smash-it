using System.IO.Compression;
using System.Xml.Linq;

namespace SmashIt.Core.Presentations;

/// <summary>
/// How large each picture of a PowerPoint file is shown, in inches, so that pictures far finer than needed can be
/// scaled down like PowerPoint's own "Compress Pictures" does.
/// </summary>
/// <remarks>
/// Only pictures placed directly on a slide, layout or master (<c>p:pic</c> outside any group) have a size we can
/// trust. A picture also used anywhere else (a group, a background, a shape fill, a table, a chart, notes…) gets no
/// size, and is never scaled down.
/// </remarks>
public static class SlideImageSizes
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>English Metric Units per inch (DrawingML's unit).</summary>
    public const double EmuPerInch = 914_400;

    /// <summary>Displayed size of a picture in inches, the whole image (cropped-away parts included).</summary>
    public readonly record struct Inches(double Width, double Height);

    /// <summary>
    /// For each media part (e.g. "ppt/media/image1.jpeg"): the largest size it is shown at, or <c>null</c> when one of
    /// its uses can't be measured.
    /// </summary>
    public static Dictionary<string, Inches?> Read(ZipArchive package)
    {
        var sizes = new Dictionary<string, Inches?>(StringComparer.OrdinalIgnoreCase);
        foreach (var relsEntry in package.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var partPath = PartOfRels(relsEntry.FullName);
            if (partPath is null) continue;

            var targets = ReadRelationships(relsEntry, partPath);
            var mediaTargets = targets.Where(t => t.Value.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase)).ToList();
            if (mediaTargets.Count == 0) continue;

            var measurable = IsSlideLike(partPath) ? MeasurePictures(package.GetEntry(partPath)) : null;
            foreach (var (id, target) in mediaTargets)
            {
                Inches? size = measurable is not null && measurable.TryGetValue(id, out var measured) ? measured : null;
                sizes[target] = Merge(sizes.TryGetValue(target, out var seen), seen, size);
            }
        }
        return sizes;
    }

    /// <summary>"ppt/slides/_rels/slide1.xml.rels" → "ppt/slides/slide1.xml"; null for the package's own rels.</summary>
    private static string? PartOfRels(string relsPath)
    {
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(relsPath))?.Replace('\\', '/') ?? "";
        var name = Path.GetFileName(relsPath)[..^".rels".Length];
        return name.Length == 0 ? null : (folder.Length > 0 ? folder + "/" : "") + name;
    }

    private static bool IsSlideLike(string partPath) =>
        partPath.StartsWith("ppt/slides/", StringComparison.OrdinalIgnoreCase) ||
        partPath.StartsWith("ppt/slideLayouts/", StringComparison.OrdinalIgnoreCase) ||
        partPath.StartsWith("ppt/slideMasters/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Relationship id → package path of its target, for internal targets.</summary>
    private static Dictionary<string, string> ReadRelationships(ZipArchiveEntry relsEntry, string partPath)
    {
        using var stream = relsEntry.Open();
        var document = XDocument.Load(stream);
        var folder = Path.GetDirectoryName(partPath)?.Replace('\\', '/') ?? "";
        var result = new Dictionary<string, string>();
        foreach (var relationship in document.Root?.Elements(Rels + "Relationship") ?? [])
        {
            if ((string?)relationship.Attribute("TargetMode") == "External") continue;
            if (relationship.Attribute("Id")?.Value is not { } id || relationship.Attribute("Target")?.Value is not { } target) continue;
            result[id] = Resolve(folder, target);
        }
        return result;
    }

    /// <summary>Resolves a relative target ("../media/image1.png") against the folder of its part.</summary>
    private static string Resolve(string folder, string target)
    {
        if (target.StartsWith('/')) return target.TrimStart('/');
        var parts = new List<string>(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var piece in target.Split('/'))
        {
            if (piece == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (piece != ".") parts.Add(piece);
        }
        return string.Join('/', parts);
    }

    /// <summary>
    /// Relationship id → displayed size, for pictures placed directly on the part; null for an id also used in a way we
    /// can't measure (inside a group, as a fill…).
    /// </summary>
    private static Dictionary<string, Inches?>? MeasurePictures(ZipArchiveEntry? entry)
    {
        if (entry is null) return null;
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);

        var result = new Dictionary<string, Inches?>();
        foreach (var blip in document.Descendants(A + "blip"))
        {
            if (blip.Attribute(R + "embed")?.Value is not { } id) continue;
            result[id] = Merge(result.TryGetValue(id, out var seen), seen, MeasureBlip(blip));
        }
        return result;
    }

    /// <summary>The larger of two uses; unknown as soon as one of them is.</summary>
    private static Inches? Merge(bool hasPrevious, Inches? previous, Inches? current) =>
        !hasPrevious ? current
        : previous is not { } p || current is not { } c ? null
        : new Inches(Math.Max(p.Width, c.Width), Math.Max(p.Height, c.Height));

    /// <summary>The size of the picture holding this blip, or null when it isn't a plain picture outside any group.</summary>
    private static Inches? MeasureBlip(XElement blip)
    {
        var blipFill = blip.Parent;
        var pic = blipFill?.Parent;
        if (blipFill?.Name != P + "blipFill" || pic?.Name != P + "pic") return null;
        // A group scales its children through its own transform: not measured.
        if (pic.Ancestors(P + "grpSp").Any()) return null;

        var ext = pic.Element(P + "spPr")?.Element(A + "xfrm")?.Element(A + "ext");
        if (ext is null || !long.TryParse((string?)ext.Attribute("cx"), out var cx) || !long.TryParse((string?)ext.Attribute("cy"), out var cy) ||
            cx <= 0 || cy <= 0)
            return null;

        // Cropping shows part of the image across the frame: the whole image is shown larger than the frame.
        var crop = blipFill.Element(A + "srcRect");
        double Percent(string name) => crop is not null && int.TryParse((string?)crop.Attribute(name), out var v) ? v / 100_000.0 : 0;
        var visibleWidth = 1 - Percent("l") - Percent("r");
        var visibleHeight = 1 - Percent("t") - Percent("b");
        if (visibleWidth <= 0.01 || visibleHeight <= 0.01) return null;

        return new Inches(cx / EmuPerInch / visibleWidth, cy / EmuPerInch / visibleHeight);
    }

    /// <summary>
    /// The long side, in pixels, an image of <paramref name="width"/>×<paramref name="height"/> needs to show at
    /// <paramref name="maxDpi"/> at <paramref name="shown"/>; 0 when it is not more than 1.5 times finer than that
    /// (the same rule as for PDFs), or when its size is unknown.
    /// </summary>
    public static int TargetLongSide(uint width, uint height, Inches? shown, int maxDpi)
    {
        if (shown is not { } s || maxDpi <= 0 || width == 0 || height == 0) return 0;
        var scale = Math.Max(s.Width * maxDpi / width, s.Height * maxDpi / height);
        if (scale <= 0 || scale > 1 / 1.5) return 0;
        return (int)Math.Ceiling(Math.Max(width, height) * scale);
    }
}
