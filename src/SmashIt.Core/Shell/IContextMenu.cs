namespace SmashIt.Core.Shell;

/// <summary>The file manager's "Compress with Smash It" entry (the Explorer menu on Windows).</summary>
public interface IContextMenu
{
    /// <summary>Whether the entry is there, whichever program it points to.</summary>
    bool IsRegistered { get; }

    /// <summary>The program the entry runs, or <c>null</c> when there is no entry.</summary>
    string? RegisteredExecutable { get; }

    /// <summary>Whether the entry runs <paramref name="executable"/>.</summary>
    bool IsRegisteredFor(string executable);

    /// <summary>Adds the entry (or points it to <paramref name="executable"/>), shown as <paramref name="label"/>.</summary>
    void Register(string executable, string label);

    void Unregister();
}
