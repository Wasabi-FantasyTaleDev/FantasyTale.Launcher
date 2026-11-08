using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class RememberedLoginTests
{
    private static readonly ServerDefinition Server = new("one", "fixture", "One", "https://one.example/", null, false, false);

    [Fact]
    public async Task PasswordMemoryRequiresSuccessfulOptInAndRemainsIndependentOfLogout()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-remember-login-").FullName;
        try
        {
            IRememberedLoginStore store = new ProtectedRememberedLoginStore(root);
            using var handler = new Handler(); using var http = new HttpClient(handler);
            using (var first = new LauncherApiClient(http, loginStore: store))
            {
                await first.LoginAsync(Server, "alice", "fixture-password");
                Assert.Null(first.RememberedLogin(Server));
                await first.LoginAsync(Server, "alice", "fixture-password", rememberPassword: true);
                Assert.Equal("fixture-password", first.RememberedLogin(Server)!.Password);
                handler.RejectLogin = true;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.LoginAsync(Server, "alice", "wrong-password", rememberPassword: true));
                Assert.Equal("fixture-password", first.RememberedLogin(Server)!.Password);
                handler.RejectLogin = false;
                await first.LogoutAsync(Server);
                Assert.Null(first.Current(Server));
                Assert.NotNull(first.RememberedLogin(Server));
            }
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "remembered-logins.bin"));
            Assert.DoesNotContain("fixture-password", Encoding.UTF8.GetString(bytes));
            using var reopened = new LauncherApiClient(http, loginStore: new ProtectedRememberedLoginStore(root));
            Assert.Null(reopened.Current(Server));
            Assert.Equal("alice", reopened.RememberedLogin(Server)!.Username);
            Assert.Null(reopened.RememberedLogin(Server with { ApiUrl = "https://replacement.example/" }));
            await reopened.LoginAsync(Server, "alice", "fixture-password", rememberPassword: false);
            Assert.Null(reopened.RememberedLogin(Server));
            Assert.False(File.Exists(Path.Combine(root, "remembered-logins.bin")));
            await reopened.LoginAsync(Server, "alice", "fixture-password", rememberPassword: true);
            reopened.Forget(Server.Id);
            Assert.Null(reopened.RememberedLogin(Server));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PasswordsRemainServerScopedAndOptOutDoesNotForgetOtherAccounts()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-remember-scope-").FullName;
        try
        {
            var store = new ProtectedRememberedLoginStore(root);
            store.Save("one", Server.ApiUrl!, "alice", "alice-password", true);
            store.Save("one", Server.ApiUrl!, "bob", "bob-password", true);
            store.Save("two", Server.ApiUrl!, "alice", "second-password", true);
            Assert.Equal("bob", store.Read("one", Server.ApiUrl!)!.Username);
            Assert.Equal("second-password", store.Read("two", Server.ApiUrl!)!.Password);
            store.Save("one", Server.ApiUrl!, "bob", "bob-password", false);
            Assert.Equal("alice", store.Read("one", Server.ApiUrl!)!.Username);
            store.Remove("one");
            Assert.Null(store.Read("one", Server.ApiUrl!));
            Assert.NotNull(store.Read("two", Server.ApiUrl!));
            File.WriteAllBytes(Path.Combine(root, "remembered-logins.bin"), [1, 2, 3]);
            Assert.Null(new ProtectedRememberedLoginStore(root).Read("two", Server.ApiUrl!));
            store.Save("two", Server.ApiUrl!, "alice", "replacement-password", true);
            Assert.Equal("replacement-password", new ProtectedRememberedLoginStore(root).Read("two", Server.ApiUrl!)!.Password);
            Assert.DoesNotContain("replacement-password", store.Read("two", Server.ApiUrl!)!.ToString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PasswordStorageRetainsAccountsBeyondFormerCapacityLimits()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-remember-bounds-").FullName;
        try
        {
            var store = new ProtectedRememberedLoginStore(root);
            for (var s = 0; s < 8; s++) for (var a = 0; a < 16; a++) store.Save("server-" + s, Server.ApiUrl!, "account-" + a, "password", true);
            store.Save("server-0", Server.ApiUrl!, "additional", "password", true);
            store.Save("ninth", Server.ApiUrl!, "account", "password", true);
            Assert.Equal("additional", new ProtectedRememberedLoginStore(root).Read("server-0", Server.ApiUrl!)!.Username);
            Assert.Equal("account", new ProtectedRememberedLoginStore(root).Read("ninth", Server.ApiUrl!)!.Username);
            store.Save("server-0", Server.ApiUrl!, "account-15", "new-password", true);
            Assert.Equal("new-password", store.Read("server-0", Server.ApiUrl!)!.Password);
            Assert.Throws<ArgumentException>(() => { if (OperatingSystem.IsWindows()) store.Save("bad/id", Server.ApiUrl!, "account", "password", true); });
            Assert.Throws<ArgumentException>(() => { if (OperatingSystem.IsWindows()) store.Save("one", "https://user:secret@example/", "account", "password", true); });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal bool RejectLogin { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body;
            if (request.RequestUri!.AbsolutePath == "/v1/catalog") body = new LauncherCatalog(1, "fixture", "Fixture", false, [], []) { HostPolicy = "domain" };
            else if (request.RequestUri.AbsolutePath == "/v1/auth/login")
            {
                if (RejectLogin) return new(HttpStatusCode.Unauthorized);
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                body = new { accessToken = "fixture-access", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    refreshToken = "fixture-refresh", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                    account = new AccountProfile(json.RootElement.GetProperty("username").GetString()!, "Fixture", new Dictionary<string, long?>(), null) };
            }
            else { Assert.Equal("/v1/auth/logout", request.RequestUri.AbsolutePath); body = new { signedOut = true }; }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        }
    }
}
