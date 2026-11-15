using System.Globalization;

namespace SmashIt.Core.Localization;

/// <summary>The languages Smash It speaks.</summary>
public enum AppLanguage
{
    /// <summary>French when Windows is in French, English otherwise.</summary>
    Automatic,
    English,
    French,
}

/// <summary>
/// English / French text. English is the default; French is used when Windows displays French,
/// unless a language is chosen explicitly with <see cref="Use"/>.
/// Strings live next to the code that uses them: <c>L10n.T("English", "Français")</c>.
/// </summary>
public static class L10n
{
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo SystemUICulture = CultureInfo.CurrentUICulture;

    /// <summary>True when the text is in French.</summary>
    public static bool IsFrench { get; private set; } = IsSystemFrench;

    /// <summary>Windows displays French.</summary>
    public static bool IsSystemFrench => SystemUICulture.TwoLetterISOLanguageName == "fr";

    /// <summary>
    /// Switches the language. An explicit choice also switches number formatting (1.5 MB / 1,5 Mo);
    /// the automatic one keeps the user's regional format.
    /// </summary>
    public static void Use(AppLanguage language)
    {
        IsFrench = language switch
        {
            AppLanguage.French => true,
            AppLanguage.English => false,
            _ => IsSystemFrench,
        };

        var culture = language switch
        {
            AppLanguage.French when SystemCulture.TwoLetterISOLanguageName != "fr" => CultureInfo.GetCultureInfo("fr-FR"),
            AppLanguage.English when SystemCulture.TwoLetterISOLanguageName != "en" => CultureInfo.GetCultureInfo("en-US"),
            _ => SystemCulture,
        };
        var uiCulture = language switch
        {
            AppLanguage.French => CultureInfo.GetCultureInfo("fr-FR"),
            AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
            _ => SystemUICulture,
        };
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
    }

    /// <summary>Picks the text for the current language.</summary>
    public static string T(string english, string french) => IsFrench ? french : english;

    /// <summary>A file size: "1.5 MB" / "1,5 Mo".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 0 => "-" + Size(-bytes),
        < 1024 => $"{bytes} {T("B", "o")}",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} {T("KB", "Ko")}",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.##} {T("MB", "Mo")}",
        _ => $"{bytes / (1024d * 1024 * 1024):0.##} {T("GB", "Go")}",
    };

    /// <summary>A ratio as a percentage: "45.9%" / "45,9 %".</summary>
    public static string Percent(double ratio) => $"{ratio * 100:0.#}{T("%", " %")}";
}
