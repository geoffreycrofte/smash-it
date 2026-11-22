using SmashIt.Core.IO;

namespace SmashIt.Core.Tests;

/// <summary>The macOS and Linux trash (Windows uses the Recycle Bin, through Visual Basic's FileSystem).</summary>
public class TrashTests
{
    [Fact]
    public void Trashinfo_paths_are_percent_encoded()
    {
        Assert.Equal("/home/léa/My%20photos/a%2Bb.png".Replace("é", "%C3%A9"), Trash.FreeDesktop.Escape("/home/léa/My photos/a+b.png"));
    }

    [Fact]
    public void A_file_goes_to_the_freedesktop_trash_with_where_it_came_from()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux only.");
        using var dir = new TempDirectory();
        var trash = dir.File("Trash");
        var first = dir.File("photo one.jpg");
        File.WriteAllText(first, "first");

        Trash.FreeDesktop.Move(first, trash);

        Assert.False(File.Exists(first));
        Assert.Equal("first", File.ReadAllText(Path.Combine(trash, "files", "photo one.jpg")));
        var info = File.ReadAllLines(Path.Combine(trash, "info", "photo one.jpg.trashinfo"));
        Assert.Equal("[Trash Info]", info[0]);
        Assert.Equal("Path=" + Trash.FreeDesktop.Escape(first), info[1]);
        Assert.Matches(@"^DeletionDate=\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d$", info[2]);

        // A second file with the same name doesn't replace the first.
        File.WriteAllText(first, "second");
        Trash.FreeDesktop.Move(first, trash);
        Assert.Equal("second", File.ReadAllText(Path.Combine(trash, "files", "photo one.2.jpg")));
        Assert.True(File.Exists(Path.Combine(trash, "info", "photo one.2.jpg.trashinfo")));
        Assert.Equal("first", File.ReadAllText(Path.Combine(trash, "files", "photo one.jpg")));
    }

    [Fact]
    public void A_file_goes_to_the_finder_trash()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS only.");
        using var dir = new TempDirectory();
        var file = dir.File($"smashit-trash-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");

        Trash.Move(file);

        Assert.False(File.Exists(file));
    }
}
