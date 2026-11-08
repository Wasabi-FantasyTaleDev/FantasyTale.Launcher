using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LoginSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-login-settings-").FullName;
    private LaunchContext Context(ServerEndpoint? endpoint)
    {
        var game = GameCatalog.Load(Path.Combine(AppContext.BaseDirectory, "games")).Single(g => g.Id == "bubblefighter");
        return new(game, game.Builds.Single(b => b.Id == "cn-279"), _root, endpoint, null);
    }

    [Theory]
    [InlineData("\r\n", "ansi")]
    [InlineData("\r", "ansi")]
    [InlineData("\n", "utf8")]
    [InlineData("\r\n", "unicode")]
    [InlineData("\r\n", "big-endian")]
    public void SynchronizesOnlyServerFieldsAndLeavesMatchingFilesUntouched(string newline, string format)
    {
        var encoding = format switch { "utf8" => new UTF8Encoding(true), "unicode" => Encoding.Unicode, "big-endian" => Encoding.BigEndianUnicode, _ => Encoding.Latin1 };
        var text = string.Join(newline, "nopatch=true", "; retained comment", "serverAddr = old.example", "serverPort=7000", "nexonMessenger=off", "");
        var path = Path.Combine(_root, "login.ini");
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
        var module = new BubbleFighterModule();
        module.PrepareLaunch(Context(new("203.0.113.42", 7210)));
        var expected = text.Replace("old.example", "203.0.113.42", StringComparison.Ordinal).Replace("7000", "7210", StringComparison.Ordinal);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray(), File.ReadAllBytes(path));
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc); File.SetLastWriteTimeUtc(path, stamp);
        module.PrepareLaunch(Context(new("203.0.113.42", 7210)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public void MissingFileAndUnspecifiedEndpointAreNotChanged()
    {
        var module = new BubbleFighterModule();
        module.PrepareLaunch(Context(new("203.0.113.42", 7210)));
        Assert.Empty(Directory.EnumerateFiles(_root));
        var path = Path.Combine(_root, "login.ini"); File.WriteAllText(path, "nopatch=true");
        module.PrepareLaunch(Context(null));
        Assert.Equal("nopatch=true", File.ReadAllText(path));
        module.PrepareLaunch(Context(new("203.0.113.42", 7210)));
        Assert.Equal("nopatch=true\nserverAddr=203.0.113.42\nserverPort=7210\n", File.ReadAllText(path));
    }

    [Fact]
    public void DuplicateFieldsAreAllUpdatedWithoutTouchingOtherKeysOrAnsiBytes()
    {
        var path = Path.Combine(_root, "login.ini");
        byte[] prefix = [0x3b, 0xd6, 0xd0, 0xce, 0xc4, 13, 10];
        File.WriteAllBytes(path, prefix.Concat(Encoding.ASCII.GetBytes("serverAddr=old\r\nSERVERADDR=other\r\nserverPort=1\r\nServerPort=2\r\notherServerAddr=keep\r\n")).ToArray());
        new BubbleFighterModule().PrepareLaunch(Context(new("203.0.113.42", 7210)));
        Assert.Equal(prefix.Concat(Encoding.ASCII.GetBytes("serverAddr=203.0.113.42\r\nSERVERADDR=203.0.113.42\r\nserverPort=7210\r\nServerPort=7210\r\notherServerAddr=keep\r\n")).ToArray(), File.ReadAllBytes(path));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
