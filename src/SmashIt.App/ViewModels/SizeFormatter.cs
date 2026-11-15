namespace SmashIt.App.ViewModels;

internal static class SizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        < 0 => "-" + Format(-bytes),
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} Ko",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.##} Mo",
        _ => $"{bytes / (1024d * 1024 * 1024):0.##} Go",
    };
}
