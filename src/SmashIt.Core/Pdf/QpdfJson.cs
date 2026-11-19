using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmashIt.Core.Pdf;

/// <summary>An image stream found in a qpdf JSON export.</summary>
internal sealed record PdfImageStream(string ObjectKey, JsonObject Dictionary, string DataFile)
{
    /// <summary>
    /// True for a plain JPEG stream (single /DCTDecode filter, no decode parameters),
    /// the only kind we rewrite ourselves.
    /// </summary>
    public bool IsPlainJpeg =>
        !Dictionary.ContainsKey("/DecodeParms") && Dictionary["/Filter"] switch
        {
            JsonValue v => v.GetValue<string>() == "/DCTDecode",
            JsonArray { Count: 1 } a => a[0]?.GetValue<string>() == "/DCTDecode",
            _ => false,
        };

    public int Width => Integer("/Width");

    public int Height => Integer("/Height");

    private int Integer(string key) =>
        Dictionary[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    /// <summary>
    /// True for an image Smash It may scale down: a plain 8-bit JPEG, grayscale or RGB (<paramref name="channels"/>
    /// tells which), without a mask or a custom decode range. CMYK, Lab or indexed images are left alone.
    /// </summary>
    public bool CanResample(Func<JsonNode?, JsonNode?> resolve, out bool grayscale)
    {
        grayscale = false;
        if (!IsPlainJpeg || Width <= 0 || Height <= 0) return false;
        if (Dictionary.ContainsKey("/Decode") || Dictionary.ContainsKey("/Mask")) return false;
        if (Dictionary["/ImageMask"] is JsonValue mask && mask.TryGetValue<bool>(out var isMask) && isMask) return false;
        if (Dictionary["/BitsPerComponent"] is JsonValue bits && bits.TryGetValue<int>(out var b) && b != 8) return false;

        var channels = resolve(Dictionary["/ColorSpace"]) switch
        {
            JsonValue v when v.TryGetValue<string>(out var name) => name switch
            {
                "/DeviceGray" => 1,
                "/DeviceRGB" => 3,
                _ => 0,
            },
            // [/ICCBased <stream>]: the profile's /N says how many channels.
            JsonArray { Count: 2 } array when array[0] is JsonValue kind && kind.TryGetValue<string>(out var k) && k == "/ICCBased" =>
                resolve(array[1]) is JsonObject profile && profile["/N"] is JsonValue n && n.TryGetValue<int>(out var count) ? count : 0,
            _ => 0,
        };
        grayscale = channels == 1;
        return channels is 1 or 3;
    }
}

/// <summary>
/// Reads a qpdf JSON v2 export (<c>--json-output --json-key=qpdf --json-stream-data=file</c>) and writes the
/// minimal JSON used by <c>--update-from-json</c>. Object numbers never change, so the rest of the PDF
/// (text, fonts, links, forms, bookmarks…) is left exactly as it was.
/// </summary>
internal sealed class QpdfJson
{
    private readonly JsonObject _objects;

    private QpdfJson(JsonObject objects) => _objects = objects;

    public static QpdfJson Load(string path)
    {
        using var stream = File.OpenRead(path);
        var root = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { MaxDepth = 1024 })
            ?? throw new InvalidDataException(L10n.T("Empty qpdf JSON export.", "Export JSON de qpdf vide."));
        var objects = root["qpdf"]?.AsArray() is { Count: >= 2 } array ? array[1]?.AsObject() : null;
        return new QpdfJson(objects ?? throw new InvalidDataException(L10n.T("Unexpected qpdf JSON export.", "Export JSON de qpdf inattendu.")));
    }

    public IEnumerable<PdfImageStream> Images()
    {
        foreach (var (key, node) in _objects)
        {
            if (!key.StartsWith("obj:", StringComparison.Ordinal)) continue;
            if (node?["stream"] is not JsonObject stream) continue;
            if (stream["dict"] is not JsonObject dict || Name(dict["/Subtype"]) != "/Image") continue;
            if (stream["datafile"]?.GetValue<string>() is not { } dataFile) continue;
            yield return new PdfImageStream(key, dict, dataFile);
        }
    }

    /// <summary>
    /// The largest size, in points, at which each image is drawn on any page (forms included), by object key.
    /// Images drawn nowhere a page shows (patterns, annotations, soft masks…) are absent.
    /// </summary>
    public Dictionary<string, (double Width, double Height)> ImageDisplaySizes()
    {
        var sizes = new Dictionary<string, (double Width, double Height)>();
        foreach (var (key, node) in _objects)
        {
            if (!key.StartsWith("obj:", StringComparison.Ordinal)) continue;
            if (node?["value"] is not JsonObject page || Name(page["/Type"]) != "/Page") continue;
            var resources = Inherited(page, "/Resources") as JsonObject;
            var content = ContentOf(page["/Contents"]);
            if (content.Length > 0) WalkContent(content, PdfMatrix.Identity, resources, sizes, depth: 0, []);
        }
        return sizes;
    }

    private void WalkContent(
        byte[] content, PdfMatrix start, JsonObject? resources,
        Dictionary<string, (double, double)> sizes, int depth, HashSet<string> forms)
    {
        var xobjects = Resolve(resources?["/XObject"]) as JsonObject;
        PdfContent.Walk(content, start, (name, ctm) =>
        {
            if (xobjects?["/" + name] is not JsonValue reference || !reference.TryGetValue<string>(out var target)) return;
            var key = "obj:" + target;
            if (_objects[key]?["stream"] is not JsonObject stream || stream["dict"] is not JsonObject dict) return;

            switch (Name(dict["/Subtype"]))
            {
                case "/Image":
                    var (width, height) = ctm.UnitSquareSize;
                    sizes[key] = sizes.TryGetValue(key, out var seen)
                        ? (Math.Max(seen.Item1, width), Math.Max(seen.Item2, height))
                        : (width, height);
                    break;
                case "/Form" when depth < 12 && forms.Add(key):
                    var matrix = Resolve(dict["/Matrix"]) is JsonArray { Count: 6 } m && m.All(v => v is JsonValue)
                        ? new PdfMatrix(Number(m[0]), Number(m[1]), Number(m[2]), Number(m[3]), Number(m[4]), Number(m[5]))
                        : PdfMatrix.Identity;
                    var formResources = Resolve(dict["/Resources"]) as JsonObject ?? resources;
                    if (ReadData(stream) is { } formContent)
                        WalkContent(formContent, matrix.Times(ctm), formResources, sizes, depth + 1, forms);
                    forms.Remove(key);
                    break;
            }
        });
    }

    /// <summary>The page's content: one stream, or several joined with a space.</summary>
    private byte[] ContentOf(JsonNode? contents)
    {
        var references = Resolve(contents) is JsonArray array ? array.ToList() : [contents];
        var parts = new List<byte>();
        foreach (var reference in references)
        {
            if (reference is not JsonValue value || !value.TryGetValue<string>(out var target)) continue;
            // A part we can't read: better to see no image at all on this page than to measure it wrong.
            if (_objects["obj:" + target]?["stream"] is not JsonObject stream || ReadData(stream) is not { } data) return [];
            parts.AddRange(data);
            parts.Add((byte)'\n');
        }
        return [.. parts];
    }

    /// <summary>
    /// The decoded data of a content stream. qpdf's JSON export keeps streams as stored, so Flate is inflated here;
    /// <c>null</c> for any other filter (rare for content streams).
    /// </summary>
    private static byte[]? ReadData(JsonObject stream)
    {
        if (stream["datafile"]?.GetValue<string>() is not { } file || !File.Exists(file)) return null;
        var dict = stream["dict"] as JsonObject;
        var filters = dict?["/Filter"] switch
        {
            null => [],
            JsonValue v when v.TryGetValue<string>(out var name) => [name],
            JsonArray array => array.Select(f => Name(f)).ToList(),
            _ => [null],
        };
        var data = File.ReadAllBytes(file);
        if (filters.Count == 0) return data;
        if (filters is not ["/FlateDecode"] || dict!.ContainsKey("/DecodeParms")) return null;
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>A page attribute, looked up through its /Parent page tree nodes when the page doesn't set it.</summary>
    private JsonNode? Inherited(JsonObject node, string attribute)
    {
        for (var depth = 0; depth < 64 && node is not null; depth++)
        {
            if (node[attribute] is { } value) return Resolve(value);
            node = Resolve(node["/Parent"]) as JsonObject;
        }
        return null;
    }

    /// <summary>Follows an indirect reference ("12 0 R") to the object's value; anything else comes back as is.</summary>
    public JsonNode? Resolve(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text) && text.EndsWith(" R", StringComparison.Ordinal) &&
            _objects["obj:" + text] is JsonObject target)
            return target["value"] ?? target["stream"]?["dict"];
        return node;
    }

    private static double Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0;

    /// <summary>True when the document carries a digital signature (any change would invalidate it).</summary>
    public bool IsSigned()
    {
        foreach (var (_, node) in _objects)
        {
            var dict = node?["value"] as JsonObject ?? node?["stream"]?["dict"] as JsonObject;
            if (dict is null) continue;
            if (Name(dict["/Type"]) == "/Sig" || dict.ContainsKey("/ByteRange")) return true;
        }
        return false;
    }

    /// <summary>Writes an update file replacing the data of the given streams; their dictionaries are kept.</summary>
    public static void WriteUpdate(string path, IEnumerable<(PdfImageStream Image, string NewDataFile)> replacements)
    {
        var objects = new JsonObject();
        foreach (var (image, dataFile) in replacements)
        {
            objects[image.ObjectKey] = new JsonObject
            {
                ["stream"] = new JsonObject
                {
                    ["dict"] = image.Dictionary.DeepClone(),
                    ["datafile"] = dataFile,
                },
            };
        }

        var root = new JsonObject
        {
            ["qpdf"] = new JsonArray(new JsonObject { ["jsonversion"] = 2 }, objects),
        };
        File.WriteAllText(path, root.ToJsonString());
    }

    private static string? Name(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
