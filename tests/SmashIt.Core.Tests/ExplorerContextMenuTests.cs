using Microsoft.Win32;
using SmashIt.Core.Shell;

namespace SmashIt.Core.Tests;

/// <summary>Writes under a throwaway HKCU\Software\SmashIt.Tests.&lt;guid&gt; key, never into the real Software\Classes.</summary>
public sealed class ExplorerContextMenuTests : IDisposable
{
    private const string Exe = @"C:\Apps\Smash It\SmashIt.exe";

    private readonly string _keyPath = $@"Software\SmashIt.Tests.{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly ExplorerContextMenu _menu;

    public ExplorerContextMenuTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_keyPath);
        _menu = new ExplorerContextMenu(_root);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Covers_the_formats_the_engines_compress_and_skips_the_others()
    {
        Assert.Contains(".jpg", ExplorerContextMenu.Extensions);
        Assert.Contains(".jpeg", ExplorerContextMenu.Extensions);
        Assert.Contains(".png", ExplorerContextMenu.Extensions);
        Assert.Contains(".heic", ExplorerContextMenu.Extensions);
        Assert.Contains(".pdf", ExplorerContextMenu.Extensions);
        Assert.Contains(".mp4", ExplorerContextMenu.Extensions);
        Assert.Contains(".mov", ExplorerContextMenu.Extensions);
        Assert.Contains(".gif", ExplorerContextMenu.Extensions);
        Assert.Contains(".svg", ExplorerContextMenu.Extensions);
        Assert.DoesNotContain(".bmp", ExplorerContextMenu.Extensions); // BMP to BMP gains nothing
    }

    [Fact]
    public void Register_writes_a_verb_per_extension_and_for_folders()
    {
        _menu.Register(Exe, "Compresser avec Smash It");

        foreach (var path in new[] { @"SystemFileAssociations\.png\shell", @"Directory\shell" })
        {
            using var verb = _root.OpenSubKey($@"{path}\{ExplorerContextMenu.VerbName}");
            Assert.NotNull(verb);
            Assert.Equal("Compresser avec Smash It", verb.GetValue("MUIVerb"));
            Assert.Equal("Player", verb.GetValue("MultiSelectModel"));
            Assert.Equal($"\"{Exe}\",0", verb.GetValue("Icon"));
            using var command = verb.OpenSubKey("command");
            Assert.Equal($"\"{Exe}\" \"%1\"", command!.GetValue(null));
        }

        Assert.True(_menu.IsRegistered);
        Assert.Equal(Exe, _menu.RegisteredExecutable);
        Assert.True(_menu.IsRegisteredFor(Exe));
        Assert.False(_menu.IsRegisteredFor(@"D:\Elsewhere\SmashIt.exe"));
    }

    [Fact]
    public void Unregister_removes_every_verb_and_keeps_the_rest()
    {
        using (var other = _root.CreateSubKey(@"SystemFileAssociations\.png\shell\SomeOtherApp")) other.SetValue("MUIVerb", "x");
        _menu.Register(Exe, "Compress with Smash It");

        _menu.Unregister();

        Assert.False(_menu.IsRegistered);
        Assert.Null(_menu.RegisteredExecutable);
        Assert.Null(_root.OpenSubKey($@"SystemFileAssociations\.jpg\shell\{ExplorerContextMenu.VerbName}"));
        Assert.NotNull(_root.OpenSubKey(@"SystemFileAssociations\.png\shell\SomeOtherApp"));
        // The keys created only for the entry are gone; the ones another app uses stay.
        Assert.Null(_root.OpenSubKey(@"SystemFileAssociations\.jpg"));
        Assert.Null(_root.OpenSubKey(@"Directory\shell"));
        Assert.NotNull(_root.OpenSubKey("SystemFileAssociations"));
    }
}
