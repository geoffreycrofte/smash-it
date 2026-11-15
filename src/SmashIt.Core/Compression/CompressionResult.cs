namespace SmashIt.Core.Compression;

public enum CompressionStatus
{
    /// <summary>A smaller file replaced (or was saved next to) the original.</summary>
    Optimized,

    /// <summary>The file was written in a new format.</summary>
    Converted,

    /// <summary>No engine produced anything smaller: the original is untouched.</summary>
    AlreadyOptimal,

    Unsupported,
    Failed,
    Cancelled,
}

public sealed record CompressionResult(
    string SourcePath,
    CompressionStatus Status,
    long OriginalSize,
    long FinalSize,
    string? OutputPath = null,
    string? CompressorId = null,
    string? Message = null)
{
    public long SavedBytes => Status is CompressionStatus.Optimized or CompressionStatus.Converted
        ? OriginalSize - FinalSize
        : 0;

    /// <summary>Saved fraction of the original size (0.25 = 25 % smaller). Negative when a conversion grew the file.</summary>
    public double SavedRatio => OriginalSize > 0 ? (double)SavedBytes / OriginalSize : 0;

    public bool IsSuccess => Status is CompressionStatus.Optimized or CompressionStatus.Converted
        or CompressionStatus.AlreadyOptimal;
}
