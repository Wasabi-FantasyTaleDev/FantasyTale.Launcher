using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerProfileRefreshConcurrencyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-server-refresh-").FullName;
    private GameDefinition Game => new("fixture", GameStatus.Enabled,
        new(new Dictionary<string, string> { ["en"] = "Fixture" }), "fixture", "external", "", null, _root, []);
    private static ServerDefinition Profile => new("community", "fixture", "Community", "https://game.example/", null, true, false);

    [Fact]
    public void RefreshCannotOverwriteAConcurrentProfileEdit()
    {
        var store = new ServerStore(Path.Combine(_root, "saved"));
        store.Save(Profile);
        var snapshot = Assert.Single(store.List([Game]));
        store.Save(Profile with { Name = "Concurrent edit" });

        Assert.Throws<InvalidOperationException>(() => store.SaveIfUnchanged(snapshot, snapshot with { Name = "Remote name" }, [Game]));
        Assert.Equal("Concurrent edit", Assert.Single(store.List([Game])).Name);

        var current = Assert.Single(store.List([Game]));
        store.SaveIfUnchanged(current, current with { Name = "Remote name" }, [Game]);
        Assert.Equal("Remote name", Assert.Single(store.List([Game])).Name);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
