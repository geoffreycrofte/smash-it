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
            ?? throw new InvalidDataException("Export JSON de qpdf vide.");
        var objects = root["qpdf"]?.AsArray() is { Count: >= 2 } array ? array[1]?.AsObject() : null;
        return new QpdfJson(objects ?? throw new InvalidDataException("Export JSON de qpdf inattendu."));
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
