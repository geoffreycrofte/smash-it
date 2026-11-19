namespace SmashIt.Core.Updates;

/// <summary>
/// A release version as tagged on GitHub: "1.2.3" or "1.2.3-beta.1" (a leading "v" is accepted).
/// A pre-release comes before the release it leads to: 1.2.3-beta.1 &lt; 1.2.3.
/// </summary>
public sealed record AppVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<AppVersion>
{
    public bool IsPreRelease => PreRelease is not null;

    public static bool TryParse(string? text, out AppVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim().TrimStart('v', 'V');
        var plus = value.IndexOf('+'); // build metadata ("0.2.0+abc123") does not count
        if (plus >= 0) value = value[..plus];
        var dash = value.IndexOf('-');
        var core = dash >= 0 ? value[..dash] : value;
        var pre = dash >= 0 ? value[(dash + 1)..] : null;
        var parts = core.Split('.');
        if (parts.Length is < 2 or > 4 || pre is "") return false;
        var numbers = new int[3];
        for (var i = 0; i < Math.Min(parts.Length, 3); i++)
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return false;
        version = new AppVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Not a version: '{text}'.");

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        return (PreRelease, other.PreRelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => ComparePreRelease(a, b),
        };
    }

    /// <summary>"beta.2" &lt; "beta.10": numeric parts compare as numbers, the others as text.</summary>
    private static int ComparePreRelease(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var bothNumbers = int.TryParse(left[i], out var x) & int.TryParse(right[i], out var y);
            var result = bothNumbers ? x.CompareTo(y) : string.CompareOrdinal(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
