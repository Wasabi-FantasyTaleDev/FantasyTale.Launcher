using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;
using Tomlyn;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class NativeCenterNamesTests
{
    [Fact]
    public void NamesKeepNativeIdentityAndEscapedUnicode()
    {
        GameServerTarget[] targets = [new("manual", "测试 \\" + '"' + "界", new("192.0.2.1", 7110)) { NativeCenterId = 2 },
            new("missing", "", new("192.0.2.2", 7110)) { NativeCenterId = 3 }];
        var metadata = NativeCenterNames.Project(targets);
        Assert.NotNull(metadata);
        Assert.Equal("bubblefighter.cn-center-names.v2", metadata.Capability);
        var model = TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(metadata.Payload)!;
        var rows = Assert.IsType<Tomlyn.Model.TomlTableArray>(model["centers"]);
        Assert.Single(rows);
        Assert.Equal(2L, rows[0]["id"]);
        Assert.Equal(targets[0].Name, rows[0]["name"]);
    }

    [Theory]
    [InlineData("cn-324")]
    [InlineData("cn-406")]
    [InlineData("cn-717")]
    [InlineData("cn-774")]
    [InlineData("kr-812")]
    public void NativeChoosersWithoutAnEnabledAdapterReceiveNoNamePatchMetadata(string build) =>
        Assert.Null(NativeCenterNames.Create(build, [new("one", "First", new("192.0.2.1", 7110)) { NativeCenterId = 1 }]));

    [Fact]
    public void Cn2011UsesDisplayOnlyCapabilityAndFullDirectoryNames()
    {
        var metadata = NativeCenterNames.Create("cn-279", [
            new("one", "Full server title", new("192.0.2.1", 7110)) { NativeCenterId = 1, ShortName = "Short" }]);
        Assert.NotNull(metadata);
        Assert.Equal("bubblefighter.cn-center-names.v2", metadata.Capability);
        Assert.Contains("Full server title", metadata.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Short", metadata.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingNamesPreserveNativeDefaults() =>
        Assert.Null(NativeCenterNames.Project([new("one", "", new("192.0.2.1", 7110)) { NativeCenterId = 1 }]));

    [Fact]
    public void DuplicateNativeIdentitiesAreRejected() =>
        Assert.Throws<InvalidDataException>(() => NativeCenterNames.Project([
            new("one", "First", new("192.0.2.1", 7110)) { NativeCenterId = 1 },
            new("two", "Second", new("192.0.2.2", 7110)) { NativeCenterId = 1 }]));

    [Fact]
    public void AutomaticManualAndUnavailableLabelsKeepTheirOwnIdentities()
    {
        GameServerTarget[] targets = [new("manual", "Manual", new("192.0.2.1", 7110)) { NativeCenterId = 2 },
            new("offline", "Unavailable", new("192.0.2.2", 7110)) { NativeCenterId = 3, UnavailableCapacity = 512 }];
        var metadata = NativeCenterNames.Project(targets, new Selection());
        var model = TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(metadata!.Payload)!;
        var rows = Assert.IsType<Tomlyn.Model.TomlTableArray>(model["centers"]);
        Assert.Equal([1L, 2L, 3L], rows.Select(row => (long)row["id"]).ToArray());
        Assert.Equal(["Automatic", "Manual", "Unavailable"], rows.Select(row => (string)row["name"]).ToArray());
    }

    private sealed class Selection : IAutomaticServerSelection
    {
        LaunchServerSelection IAutomaticServerSelection.Grant => throw new NotSupportedException();
        IReadOnlyList<GameServerTarget> IAutomaticServerSelection.Targets => throw new NotSupportedException();
        ServerSelector IAutomaticServerSelection.Automatic => new(1, "Automatic", true, []);
        IReadOnlyList<string> IAutomaticServerSelection.AdmittedCenterIds => throw new NotSupportedException();
        Task<IReadOnlyList<AutomaticServerCandidate>?> IAutomaticServerSelection.RefreshAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
