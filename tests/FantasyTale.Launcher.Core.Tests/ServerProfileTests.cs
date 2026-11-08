using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerProfileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-server-profile-").FullName;
    private static ServerDefinition Profile => new("community", "fixture", "Community", "https://game.example/", new("127.0.0.1", 8080), true, false)
    {
        Builds = ["kr-1"], PreferIp = false,
        ClientPolicy = new(1, [new("community", "http", "https://store.example/", "source.toml")], [new("fixture.language", [new("need", "require", ["kr-1"])])]),
        Presentation = new()
        {
            MotdTranslations = new Dictionary<string, string> { ["zh"] = "First line\nSecond line", ["en"] = "English" },
            SummaryTranslations = new Dictionary<string, string> { ["ko"] = "Short" },
            IconUrlTranslations = new Dictionary<string, string> { ["en"] = "https://images.example/icon.png" },
            ImagesTranslations = new Dictionary<string, IReadOnlyList<string>> { ["zh"] = ["https://images.example/one.png"] },
            OnlinePlayers = -1, PeakOnlinePlayers = 12, PlayerLimit = 24, State = "maintenance", Whitelist = true, RosterEnabled = false,
        },
    };
    private GameDefinition Game => new("fixture", GameStatus.Enabled, new(new Dictionary<string, string> { ["en"] = "Fixture" }), "fixture", "external", "", null, _root, []);

    [Fact]
    public async Task ExportAndImportRoundTripAllLanguagesArtworkAndDeclarationsAsReadableToml()
    {
        var text = ServerProfileFile.Format(Profile);
        Assert.Contains("[presentation.motd_translations]", text);
        Assert.Contains("[[client_policy_rules]]", text);
        Assert.DoesNotContain("password", text);
        var file = Path.Combine(_root, "Any name.toml");
        await ServerProfileFile.ExportAsync(file, text);
        var parsed = ServerProfileFile.Parse(file, await ServerProfileFile.ReadAsync(file));
        Assert.Equal(text, ServerProfileFile.Format(parsed));
        var store = new ServerStore(Path.Combine(_root, "saved"));
        var inspection = store.InspectImport(parsed, [Game]);
        Assert.False(inspection.Exists);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "saved"), "*.toml"));
        store.Import(parsed, [Game]);
        Assert.True(File.Exists(Path.Combine(_root, "saved", "community.toml")));
        Assert.Equal(text, ServerProfileFile.Format(Assert.Single(store.List([Game]))));
    }

    [Theory]
    [InlineData("native", false)]
    [InlineData("native", true)]
    [InlineData("gateway", false)]
    [InlineData("gateway", true)]
    public void NativeDirectoryRoundTripsInsideTheSavedServerProfile(string mode, bool enabled)
    {
        var configured = Profile with
        {
            NativeDirectory = new(mode, ["cn-324"],
            [
                new("official", "Formal server", new("8.155.133.217", 7140)),
                new("test", "Test server", new("bf-test.600819.xyz", 7140)),
            ]) { Enabled = enabled },
        };

        var text = ServerProfileFile.Format(configured);
        var parsed = ServerProfileFile.Parse("native-directory.toml", text);
        var saved = new ServerStore(Path.Combine(_root, "saved"));
        saved.Save(parsed);

        Assert.Contains("[server.native_directory]", text);
        Assert.Contains("[[server.native_directory.targets]]", text);
        Assert.Equal(configured.NativeDirectory!.Mode, parsed.NativeDirectory!.Mode);
        Assert.Equal(enabled, parsed.NativeDirectory.Enabled);
        Assert.Equal(configured.NativeDirectory.Builds, parsed.NativeDirectory.Builds);
        Assert.Equal(configured.NativeDirectory.Targets, parsed.NativeDirectory.Targets);
        Assert.Equal(text, ServerProfileFile.Format(Assert.Single(saved.List([Game]))));
    }

    [Fact]
    public void NativeDirectoryRejectsUnsupportedModeAndDuplicateTargets()
    {
        var target = new GameServerTarget("same", "One", new("127.0.0.1", 7140));
        var valid = new NativeServerDirectory("native", ["cn-324"], [target, target with { Name = "Two" }]);

        Assert.Throws<ArgumentException>(() => ServerStore.Validate(Profile with { NativeDirectory = valid }));
        Assert.Throws<ArgumentException>(() => ServerStore.Validate(Profile with
        {
            NativeDirectory = new("unsupported", ["cn-324"], [target]),
        }));
    }

    [Fact]
    public void ImportedCenterRequiresEnablementAndDefaultsItsEnabledModeToGateway()
    {
        var profile = Profile with
        {
            NativeDirectory = new("gateway", ["cn-324"], [new("one", "One", new("127.0.0.1", 7140))]) { Enabled = true },
        };
        var text = ServerProfileFile.Format(profile).Replace("mode = \"gateway\"\n", "", StringComparison.Ordinal);
        var enabled = ServerProfileFile.Parse("enabled-default-mode.toml", text);
        Assert.True(enabled.NativeDirectory!.Enabled);
        Assert.Equal("gateway", enabled.NativeDirectory.Mode);
        var disabled = ServerProfileFile.Parse("disabled-default-mode.toml", text.Replace("enabled = true\n", "", StringComparison.Ordinal));
        Assert.False(disabled.NativeDirectory!.Enabled);
        Assert.Equal("gateway", disabled.NativeDirectory.Mode);
    }

    [Fact]
    public void DifferentDataRequiresConfirmationAndStaleConfirmationCannotOverwriteLaterChanges()
    {
        var store = new ServerStore(Path.Combine(_root, "saved")); store.Save(Profile);
        var changed = Profile with { Name = "New name" };
        var inspection = store.InspectImport(changed, [Game]);
        Assert.True(inspection.Exists); Assert.False(inspection.Identical);
        Assert.Throws<InvalidOperationException>(() => store.Import(changed, [Game]));
        Assert.Equal(Profile.Name, Assert.Single(store.List([Game])).Name);
        store.Save(Profile with { Name = "Concurrent edit" });
        Assert.Throws<InvalidOperationException>(() => store.Import(changed, [Game], inspection.Fingerprint));
        Assert.Equal("Concurrent edit", Assert.Single(store.List([Game])).Name);
        var current = store.InspectImport(changed, [Game]);
        store.Import(changed, [Game], current.Fingerprint);
        Assert.Equal("New name", Assert.Single(store.List([Game])).Name);
        Assert.True(store.InspectImport(changed, [Game]).Identical);
    }

    [Fact]
    public void EndpointMatchKeepsExistingIdentityAndRenamedFileWhileIdenticalImportDoesNotRewrite()
    {
        var folder = Path.Combine(_root, "saved"); var store = new ServerStore(folder); store.Save(Profile);
        var original = Directory.GetFiles(folder, "*.toml").Single(); var renamed = Path.Combine(folder, "Renamed.toml"); File.Move(original, renamed);
        var modified = Profile with { Id = "another-source", Name = "Changed" };
        var inspection = store.InspectImport(modified, [Game]);
        Assert.Equal(Profile.Id, inspection.Server.Id);
        store.Import(modified, [Game], inspection.Fingerprint);
        Assert.Equal(renamed, Directory.GetFiles(folder, "*.toml").Single());
        var timestamp = File.GetLastWriteTimeUtc(renamed);
        store.Import(modified, [Game]);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(renamed));
        Assert.Throws<ArgumentException>(() => store.Import(Profile with { Game = "other-game" }, [Game]));
    }

    [Fact]
    public async Task CanceledExportAndMalformedImportPreserveExistingData()
    {
        var file = Path.Combine(_root, "export.toml"); await File.WriteAllTextAsync(file, "Previous");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ServerProfileFile.ExportAsync(file, ServerProfileFile.Format(Profile), canceled.Token));
        Assert.Equal("Previous", await File.ReadAllTextAsync(file));
        Assert.Single(Directory.GetFiles(_root));
        Assert.Throws<DefinitionException>(() => ServerProfileFile.Parse("bad.toml", ServerProfileFile.Format(Profile) + "\n[credentials]\npassword=\"secret\""));
    }

    [Theory]
    [InlineData("127_0_0_1_8080")]
    [InlineData("FantasyTale")]
    [InlineData("community-Server_2")]
    [InlineData("COM0")]
    public void FileSafeServerIdentitiesAreAcceptedWithoutUnrelatedClientIdRestrictions(string id)
    {
        ServerStore.ValidateId(id);
        Assert.Equal(id + ".toml", ServerProfileFile.FileName(Profile with { Id = id }));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("服务器")]
    [InlineData("café")]
    [InlineData("")]
    [InlineData("server.example")]
    [InlineData("../outside")]
    [InlineData("CON")]
    [InlineData("lpt1")]
    [InlineData("server:8080")]
    public void UnsafeServerIdentitiesAreRejected(string id) => Assert.Throws<ArgumentException>(() => ServerStore.ValidateId(id));

    [Fact]
    public void DefaultIdentityUsesTheInitialEndpointButAddressUpdatesRetainIdentity()
    {
        Assert.Equal("127_0_0_1_8080", ServerStore.DefaultId(null, new("127.0.0.1", 8080)));
        Assert.Equal("game_example_443", ServerStore.DefaultId("https://game.example/", null));
        Assert.Equal("__1_443", ServerStore.DefaultId("https://[::1]/", null));
        var store = new ServerStore(Path.Combine(_root, "saved"));
        var original = Profile with { Id = "127_0_0_1_8080" }; store.Import(original, [Game]);
        var changed = original with { Endpoint = new("203.0.113.1", 10101), ApiUrl = "https://new.example/" };
        var comparison = store.InspectImport(changed, [Game]);
        Assert.True(comparison.Exists);
        store.Import(changed, [Game], comparison.Fingerprint);
        Assert.Equal(original.Id, Assert.Single(store.List([Game])).Id);
        Assert.Equal(original.Id + ".toml", Path.GetFileName(Directory.GetFiles(Path.Combine(_root, "saved"), "*.toml").Single()));
    }

    [Fact]
    public void CreateOnlySaveRejectsCaseInsensitiveIdentityCollisionsWithoutRewritingTheOriginal()
    {
        var folder = Path.Combine(_root, "saved"); var store = new ServerStore(folder);
        store.Save(Profile, createOnly: true);
        var file = Directory.GetFiles(folder, "*.toml").Single();
        var original = File.ReadAllText(file);
        Assert.Throws<InvalidOperationException>(() => store.Save(Profile with { Id = Profile.Id.ToUpperInvariant(), Name = "Replacement" }, createOnly: true));
        Assert.Equal(original, File.ReadAllText(file));
        Assert.Single(store.List([Game]));
        store.Save(Profile with { Name = "Explicit edit" });
        Assert.Equal("Explicit edit", Assert.Single(store.List([Game])).Name);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
