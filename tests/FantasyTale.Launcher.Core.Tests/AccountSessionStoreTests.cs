using System.Text;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AccountSessionStoreTests
{
    [Fact]
    public void WindowsProtectedStoreReopensWithoutPlaintextCredentialsAndKeepsOtherServers()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "ft-account-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new ProtectedAccountSessionStore(root);
            var alice = Entry("one", "alice", "test-refresh-secret-alice");
            var bob = Entry("two", "bob", "test-refresh-secret-bob");
            first.Save("one", alice.ApiUrl, "alice", [alice]);
            first.Save("two", bob.ApiUrl, "bob", [bob]);
            var bytes = File.ReadAllBytes(Path.Combine(root, "account-sessions.bin"));
            Assert.DoesNotContain(alice.RefreshToken, Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain(bob.RefreshToken, Encoding.UTF8.GetString(bytes));
            var reopened = new ProtectedAccountSessionStore(root);
            Assert.Equal(2, reopened.Load().Count);
            Assert.Equal(alice.RefreshToken, reopened.Load().Single(value => value.ServerId == "one").RefreshToken);
            reopened.Remove("one");
            Assert.Equal("bob", Assert.Single(new ProtectedAccountSessionStore(root).Load()).Username);
            reopened.Remove("two");
            Assert.Empty(reopened.Load());
            Assert.False(File.Exists(Path.Combine(root, "account-sessions.bin")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptProtectedFileCannotRestoreCredentialsAndCanBeReplacedByExplicitLogin()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "ft-account-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProtectedAccountSessionStore(root);
            File.WriteAllBytes(Path.Combine(root, "account-sessions.bin"), [1, 2, 3, 4]);
            Assert.Empty(store.Load());
            var alice = Entry("one", "alice", "test-new-refresh");
            store.Save("one", alice.ApiUrl, "alice", [alice]);
            Assert.Equal("alice", Assert.Single(store.Load()).Username);
            Assert.Throws<ArgumentException>(() =>
            {
                if (OperatingSystem.IsWindows()) store.Save("wrong-server", alice.ApiUrl, "alice", [alice]);
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ProtectedSessionsRetainLargeProfilesAndAllAccountsAcrossRestart()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-account-capacity-").FullName;
        try
        {
            var entries = Enumerable.Range(0, 145).Select(i => Entry("one", "user" + i, "refresh-" + i) with
            {
                Selected = i == 144,
                Profile = new AccountProfile("user" + i, "user" + i, Enumerable.Range(0, 1024)
                    .ToDictionary(n => "field" + n + new string('x', 40), n => (long?)n), null),
            }).ToArray();
            new ProtectedAccountSessionStore(root).Save("one", entries[0].ApiUrl, "user144", entries);
            Assert.True(new FileInfo(Path.Combine(root, "account-sessions.bin")).Length > 4 * 1024 * 1024);
            var reopened = new ProtectedAccountSessionStore(root);
            var restored = reopened.Load();
            Assert.Equal(145, restored.Count);
            Assert.Equal("user144", Assert.Single(restored, entry => entry.Selected).Username);
            Assert.All(restored, entry => Assert.Equal(1024, entry.Profile.Stats.Count));
            using var api = new LauncherApiClient(sessionStore: reopened);
            var server = new ServerDefinition("one", "fixture", "Fixture", entries[0].ApiUrl, null, false, false);
            Assert.Equal(145, api.Accounts(server).Accounts.Count);
            Assert.Equal("user144", api.Current(server)!.Username);
            reopened.Remove("one");
            Assert.Empty(reopened.Load());
        }
        finally { Directory.Delete(root, true); }
    }

    private static PersistedAccountSession Entry(string server, string username, string token) =>
        new(server, "https://" + server + ".example/", username, token, DateTimeOffset.UtcNow.AddDays(7),
            new AccountProfile(username, username, new Dictionary<string, long?> { ["level"] = 12 }, null), true);
}
