using System.Globalization;
using System.Text;

namespace SmashIt.Core.Pdf;

/// <summary>A PDF transformation matrix [a b c d e f].</summary>
internal readonly record struct PdfMatrix(double A, double B, double C, double D, double E, double F)
{
    public static PdfMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary><c>this × other</c>: what <c>cm</c> does with <c>this</c> as operand and <c>other</c> as the current matrix.</summary>
    public PdfMatrix Times(PdfMatrix m) => new(
        A * m.A + B * m.C,
        A * m.B + B * m.D,
        C * m.A + D * m.C,
        C * m.B + D * m.D,
        E * m.A + F * m.C + m.E,
        E * m.B + F * m.D + m.F);

    /// <summary>Size, in points, of the unit square (an image) drawn with this matrix.</summary>
    public (double Width, double Height) UnitSquareSize => (Math.Sqrt(A * A + B * B), Math.Sqrt(C * C + D * D));
}

/// <summary>
/// Reads PDF content streams just enough to know where images are drawn: the graphics state stack
/// (<c>q</c>/<c>Q</c>), matrix changes (<c>cm</c>) and XObject calls (<c>Do</c>). Text, paths and colors are skipped.
/// </summary>
internal static class PdfContent
{
    public abstract record Token;

    public sealed record Number(double Value) : Token;

    public sealed record Name(string Value) : Token;

    public sealed record Operator(string Value) : Token;

    /// <summary>Strings, arrays, dictionaries: operands we don't need to read.</summary>
    public sealed record Other : Token;

    /// <summary>
    /// Calls <paramref name="onDo"/> with the XObject name and the current matrix for every <c>Do</c>.
    /// </summary>
    public static void Walk(byte[] content, PdfMatrix start, Action<string, PdfMatrix> onDo)
    {
        var stack = new Stack<PdfMatrix>();
        var ctm = start;
        var operands = new List<Token>();
        foreach (var token in Tokenize(content))
        {
            if (token is not Operator op)
            {
                operands.Add(token);
                continue;
            }
            switch (op.Value)
            {
                case "q":
                    stack.Push(ctm);
                    break;
                case "Q":
                    if (stack.Count > 0) ctm = stack.Pop();
                    break;
                case "cm" when operands.Count >= 6 && operands[^6..].All(o => o is Number):
                    var n = operands[^6..].Cast<Number>().Select(x => x.Value).ToArray();
                    ctm = new PdfMatrix(n[0], n[1], n[2], n[3], n[4], n[5]).Times(ctm);
                    break;
                case "Do" when operands.Count >= 1 && operands[^1] is Name name:
                    onDo(name.Value, ctm);
                    break;
            }
            operands.Clear();
        }
    }

    /// <summary>Splits a content stream into tokens; inline images (<c>BI … ID … EI</c>) are skipped whole.</summary>
    public static IEnumerable<Token> Tokenize(byte[] data)
    {
        var i = 0;
        while (true)
        {
            SkipSpaceAndComments(data, ref i);
            if (i >= data.Length) yield break;
            var c = data[i];
            switch (c)
            {
                case (byte)'(':
                    SkipLiteralString(data, ref i);
                    yield return new Other();
                    break;
                case (byte)'<' when i + 1 < data.Length && data[i + 1] == '<':
                case (byte)'>' when i + 1 < data.Length && data[i + 1] == '>':
                case (byte)'[' or (byte)']' or (byte)'{' or (byte)'}':
                    // Dictionaries and arrays: their content comes as tokens of its own, then the operator
                    // that follows clears the operands anyway.
                    i += c is (byte)'<' or (byte)'>' ? 2 : 1;
                    yield return new Other();
                    break;
                case (byte)'<':
                    while (i < data.Length && data[i] != '>') i++;
                    i++;
                    yield return new Other();
                    break;
                case (byte)'/':
                    var start = ++i;
                    while (i < data.Length && IsRegular(data[i])) i++;
                    yield return new Name(Encoding.Latin1.GetString(data, start, i - start));
                    break;
                default:
                    var begin = i;
                    while (i < data.Length && IsRegular(data[i])) i++;
                    if (i == begin) { i++; continue; } // stray delimiter
                    var word = Encoding.Latin1.GetString(data, begin, i - begin);
                    if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                        word.AsSpan().IndexOfAny("eE") < 0)
                    {
                        yield return new Number(number);
                    }
                    else if (word == "ID")
                    {
                        SkipInlineImageData(data, ref i);
                        yield return new Operator("EI");
                    }
                    else
                    {
                        yield return new Operator(word);
                    }
                    break;
            }
        }
    }

    private static bool IsSpace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
        or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static bool IsRegular(byte b) => !IsSpace(b) && !IsDelimiter(b);

    private static void SkipSpaceAndComments(byte[] data, ref int i)
    {
        while (i < data.Length)
        {
            if (IsSpace(data[i])) i++;
            else if (data[i] == '%') { while (i < data.Length && data[i] is not (10 or 13)) i++; }
            else return;
        }
    }

    private static void SkipLiteralString(byte[] data, ref int i)
    {
        var depth = 0;
        for (; i < data.Length; i++)
        {
            switch (data[i])
            {
                case (byte)'\\': i++; break;
                case (byte)'(': depth++; break;
                case (byte)')':
                    if (--depth == 0) { i++; return; }
                    break;
            }
        }
    }

    /// <summary>After <c>ID</c>: one space, then binary data up to an <c>EI</c> standing on its own.</summary>
    private static void SkipInlineImageData(byte[] data, ref int i)
    {
        i++;
        for (; i + 1 < data.Length; i++)
        {
            if (data[i] == 'E' && data[i + 1] == 'I' && IsSpace(data[i - 1]) &&
                (i + 2 >= data.Length || IsSpace(data[i + 2]) || IsDelimiter(data[i + 2])))
            {
                i += 2;
                return;
            }
        }
        i = data.Length;
    }
}
