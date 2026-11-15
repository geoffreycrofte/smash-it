using System.Runtime.CompilerServices;

namespace SmashIt.Core.Tests;

internal static class TestLanguage
{
    /// <summary>Messages are checked in English, whatever the language of the machine running the tests.</summary>
#pragma warning disable CA2255 // ModuleInitializer in a test assembly is intended
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseEnglish() => L10n.Use(AppLanguage.English);
}
