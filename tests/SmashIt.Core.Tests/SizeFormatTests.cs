namespace SmashIt.Core.Tests;

public class SizeFormatTests
{
    [Theory]
    [InlineData(999, @"^999 (B|o)$")]
    [InlineData(999 * 1024, @"^999 (KB|Ko)$")]
    [InlineData(1003 * 1024 + 400, @"^0[.,]98 (MB|Mo)$")] // not "1003.4 KB"
    [InlineData(1_450_000, @"^1[.,]38 (MB|Mo)$")]
    [InlineData(1000L * 1024 * 1024, @"^0[.,]98 (GB|Go)$")]
    public void A_size_never_has_four_digits(long bytes, string pattern) =>
        Assert.Matches(pattern, L10n.Size(bytes));
}
