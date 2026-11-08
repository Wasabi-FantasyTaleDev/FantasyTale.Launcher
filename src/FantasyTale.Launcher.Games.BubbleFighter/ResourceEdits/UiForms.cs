using FantasyTale.Launcher.Games.BubbleFighter.Archives;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Read-only access to native .ui forms for client plugins that need layout parameters (positions, sizes, text keys) of
/// controls they draw beside or address by path in resource edits.
/// </summary>
public interface IGameUiForms
{
    /// <summary>Lists .ui members of the build's default UI archive in archive order.</summary>
    /// <param name="clientFolder">Client root directory of the instance.</param>
    /// <param name="build">Client build identity, for example kr-812.</param>
    IReadOnlyList<string> List(string clientFolder, string build);

    /// <summary>Parses one form of the build's default UI archive into its control tree.</summary>
    /// <param name="clientFolder">Client root directory of the instance.</param>
    /// <param name="build">Client build identity, for example kr-812.</param>
    /// <param name="member">Form member path such as Ui/MainMenuStage2.ui.</param>
    IReadOnlyList<UiControl> Read(string clientFolder, string build, string member);

    /// <summary>Parses form bytes already read by the caller, with the same strict decoding as resource edits.</summary>
    /// <param name="bytes">Original member bytes including any byte order mark.</param>
    /// <param name="member">Member path used in error messages.</param>
    IReadOnlyList<UiControl> Parse(byte[] bytes, string member);
}

/// <summary>Default UI form reader resolving the archive from the build's built-in resource targets.</summary>
internal sealed class UiForms : IGameUiForms
{
    /// <inheritdoc />
    public IReadOnlyList<string> List(string clientFolder, string build)
    {
        using var archive = Open(clientFolder, build);
        return archive.Entries.Select(e => e.Path).Where(p => p.EndsWith(".ui", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<UiControl> Read(string clientFolder, string build, string member)
    {
        ArchiveResourceComposer.ValidatePath(member);
        using var archive = Open(clientFolder, build);
        return Parse(archive.Read(member), member);
    }

    /// <inheritdoc />
    public IReadOnlyList<UiControl> Parse(byte[] bytes, string member) => UiDocument.Load(bytes, member).Controls();

    private static IArchiveReader Open(string clientFolder, string build)
    {
        if (!ResourceTargets.Defaults(build, null).TryGetValue("ui", out var target))
            throw new InvalidDataException("No default UI archive is known for build " + build);
        return Archive.Open(ArchiveResourceComposer.SafeFile(clientFolder, target.Archive), target.Archive);
    }
}
