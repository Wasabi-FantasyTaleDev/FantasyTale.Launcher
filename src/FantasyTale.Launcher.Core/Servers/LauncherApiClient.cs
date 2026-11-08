using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Bounded version-one API client with process-local access tokens and optional protected refresh sessions scoped to an exact server URL.</summary>
public sealed partial class LauncherApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private readonly HttpClient _http;
    private readonly NetworkPolicy _network;
    private readonly TimeProvider _clock;
    private readonly IAccountSessionStore? _sessionStore;
    private readonly IRememberedLoginStore? _loginStore;
    private readonly Dictionary<string, (string? Policy, string? Region, string? Minimum, ClientPolicy Plugins)> _policies = new(StringComparer.Ordinal);
    // Secure base URLs servers publish in their catalogs, keyed by the address as entered.
    private readonly Dictionary<string, string> _secureUrls = new(StringComparer.Ordinal);
    private readonly bool _ownsHttp;
    private readonly Dictionary<string, ServerSessions> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, string Url, string Game), CatalogSnapshot> _catalogs = new();
    private readonly object _gate = new();
    private readonly string _launcherVersion;
    private sealed class CatalogSnapshot
    {
        internal long Started;
        internal long Applied;
        internal LauncherCatalog? Value;
    }
    private sealed class Session(string token, DateTimeOffset expiresAt, string? refreshToken, DateTimeOffset? refreshExpiresAt, AccountProfile profile)
    {
        internal string Token { get; set; } = token;
        internal DateTimeOffset ExpiresAt { get; set; } = expiresAt;
        internal string? RefreshToken { get; set; } = refreshToken;
        internal DateTimeOffset? RefreshExpiresAt { get; set; } = refreshExpiresAt;
        internal AccountProfile Profile { get; set; } = profile;
        internal Guid Identity { get; } = Guid.NewGuid();
        internal SemaphoreSlim RefreshGate { get; } = new(1, 1);
        internal string? MinimumLauncherVersion { get; set; }
        internal ClientPolicy? ClientPolicy { get; set; }
        internal bool InvalidPolicy { get; set; }
    }
    private sealed class ServerSessions(string apiUrl)
    {
        internal string ApiUrl { get; } = apiUrl;
        internal Dictionary<string, Session> Accounts { get; } = new(StringComparer.Ordinal);
        internal string? Selected { get; set; }
        internal long Revision { get; set; }
        internal int PendingLogins { get; set; }
    }
    private sealed record Selection(ServerSessions Owner, Session Session, long Revision);
    private sealed class AuthResponse
    {
        public string? AccessToken { get; set; }
        public DateTimeOffset? AccessExpiresAt { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset? RefreshExpiresAt { get; set; }
        public AccountProfile? Account { get; set; }
        public string? MinimumLauncherVersion { get; set; }
        public ClientPolicy ClientPolicy { get; set; } = ClientPolicy.Empty;
    }

    /// <summary>Creates the client; injected HTTP clients are owned by their caller and must prohibit redirects.</summary>
    public LauncherApiClient(HttpClient? http = null, NetworkPolicy? network = null, TimeProvider? clock = null, IAccountSessionStore? sessionStore = null, IRememberedLoginStore? loginStore = null, string? launcherVersion = null)
    {
        _network = network ?? new NetworkPolicy();
        _launcherVersion = launcherVersion ?? LauncherCompatibility.CurrentVersion;
        _ = PluginCatalog.CompareVersions(_launcherVersion, _launcherVersion);
        _clock = clock ?? TimeProvider.System;
        _sessionStore = sessionStore;
        _loginStore = loginStore;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(NetworkPolicy.CreateHttpHandler()) { Timeout = TimeSpan.FromSeconds(20) };
        RestoreSessions();
    }

    private void RestoreSessions()
    {
        if (_sessionStore is null) return;
        foreach (var entry in _sessionStore.Load())
        {
            try
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.ServerId) || string.IsNullOrWhiteSpace(entry.ApiUrl)) continue;
                ServerStore.ValidateId(entry.ServerId);
                _ = ServerStore.ApiBase(entry.ApiUrl);
                var apiUrl = entry.ApiUrl;
                if (string.IsNullOrWhiteSpace(entry.Username) || entry.Username.Length > 64 || entry.RefreshToken is null || !ValidToken(entry.RefreshToken) || entry.RefreshExpiresAt <= _clock.GetUtcNow() || entry.RefreshExpiresAt > _clock.GetUtcNow().AddDays(365)) continue;
                ValidateProfile(entry.Profile);
                if (entry.Profile.Username != entry.Username) continue;
                ValidateRefresh(entry.RefreshToken, entry.RefreshExpiresAt, _clock.GetUtcNow());
                lock (_gate)
                {
                    var owner = _sessions.GetValueOrDefault(entry.ServerId);
                    if (owner is null) _sessions[entry.ServerId] = owner = new ServerSessions(apiUrl);
                    if (owner.ApiUrl != apiUrl || owner.Accounts.ContainsKey(entry.Username)) continue;
                    owner.Accounts.Add(entry.Username, new Session("", _clock.GetUtcNow(), entry.RefreshToken, entry.RefreshExpiresAt, entry.Profile));
                    if (entry.Selected) owner.Selected = entry.Username;
                }
            }
            catch (ArgumentException) { }
            catch (InvalidDataException) { }
        }
    }

    private void PersistOwner(string serverId, ServerSessions owner)
    {
        if (_sessionStore is null) return;
        lock (_gate)
        {
            // Serialize snapshots with logout and server removal so a late save cannot restore a revoked selection.
            if (_sessions.GetValueOrDefault(serverId) != owner) return;
            var selected = owner.Selected;
            var sessions = owner.Accounts.Where(pair => pair.Value.RefreshToken is not null && pair.Value.RefreshExpiresAt > _clock.GetUtcNow())
                .Select(pair => new PersistedAccountSession(serverId, owner.ApiUrl, pair.Key, pair.Value.RefreshToken!, pair.Value.RefreshExpiresAt!.Value, pair.Value.Profile, pair.Key == selected)).ToArray();
            _sessionStore.Save(serverId, owner.ApiUrl, selected, sessions);
        }
    }

    /// <summary>Loads and validates server-provided instances and registration controls.</summary>
    public Task<LauncherCatalog> CatalogAsync(ServerDefinition server, CancellationToken ct = default) => CatalogCoreAsync(server, ct);

    /// <summary>Reuses this endpoint's last validated discovery during launch; explicit catalog refreshes still access the server.</summary>
    public LauncherCatalog? CachedCatalog(ServerDefinition server)
    {
        lock (_gate)
        {
            if (server.ApiUrl is null || _catalogs.GetValueOrDefault((server.Id, server.ApiUrl, server.Game))?.Value is not { } value) return null;
            LauncherCompatibility.Require(value.MinimumLauncherVersion, server.Name, _launcherVersion);
            return value;
        }
    }

    /// <summary>Reads only public display names and optional levels after the user opens the roster.</summary>
    public async Task<OnlinePlayerList> OnlinePlayersAsync(ServerDefinition server, CancellationToken ct = default)
    {
        var result = await SendAsync<OnlinePlayerList>(server, HttpMethod.Get, "v1/players", null, null, ct).ConfigureAwait(false);
        if (result.Schema != 1 || result.Players is null || result.Players.Any(player => player is null ||
            string.IsNullOrWhiteSpace(player.Name) || player.Name.Any(char.IsControl) || player.Level < 0
            || player.BuildId is { } build && (build.Length is < 1 or > 128 || build.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))))
            throw new InvalidDataException("Invalid public player list.");
        return result.Enabled ? result : result with { Players = [] };
    }

    private async Task<LauncherCatalog> CatalogCoreAsync(ServerDefinition server, CancellationToken ct, bool allowIncompatible = false)
    {
        var apiUrl = server.ApiUrl ?? throw new InvalidOperationException("Configure this server's launcher API address first.");
        var key = (server.Id, apiUrl, server.Game);
        CatalogSnapshot snapshot;
        long revision;
        string? discoveryPolicy;
        lock (_gate)
        {
            if (!_catalogs.TryGetValue(key, out snapshot!)) _catalogs[key] = snapshot = new();
            revision = ++snapshot.Started;
            discoveryPolicy = _policies.GetValueOrDefault(apiUrl).Policy;
        }
        async Task<(JsonElement Response, LauncherCatalog Catalog)> Read(string? policy)
        {
            var json = await SendAsync<JsonElement>(server, HttpMethod.Get, "v1/catalog", null, null, ct, policy).ConfigureAwait(false);
            try { return (json, json.Deserialize<LauncherCatalog>(Json) ?? throw new InvalidDataException("Empty API response.")); }
            catch (JsonException) { throw new InvalidDataException("Malformed launcher API response."); }
        }
        JsonElement response;
        LauncherCatalog catalog;
        // Honor explicit local routing before discovery. The China-only default cannot
        // assume an unknown region; the returned server policy controls authenticated work.
        var initialPolicy = discoveryPolicy ?? _network.InitialDiscoveryPolicy(server);
        try { (response, catalog) = await Read(initialPolicy).ConfigureAwait(false); }
        catch (Exception e) when (!ct.IsCancellationRequested && discoveryPolicy is not ("ip" or "domain") && server.ApiUrl is { } url && ServerStore.ApiBase(url).Scheme == "http" &&
            !IPAddress.TryParse(ServerStore.ApiBase(url).DnsSafeHost, out _) && e is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            // Discovery carries no credentials and must work before a server can declare its routing policy.
            (response, catalog) = await Read(initialPolicy == "ip" ? "domain" : "ip").ConfigureAwait(false);
        }
        if (catalog.Schema != 1 || catalog.Game != server.Game || catalog.Name is null ||
            catalog.Instances is null || catalog.RegistrationFields is null)
            throw new InvalidDataException("The server returned an incompatible catalog.");
        ServerCenterRouting.ValidateConfiguration(catalog.Center);
        foreach (var preset in catalog.Instances)
        {
            if (preset is null || string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Build) || preset.Name is null ||
                preset.Name.Values.Any(v => v is null) || preset.LauncherLogin is not (null or "required" or "optional" or "none" or "game") || preset.ReleaseVersion is { } release && release.Any(char.IsControl)) throw new InvalidDataException("Invalid server instance preset.");
            if (preset.ProtocolVersion <= 0 || preset.PatchVersion is { } patch && (string.IsNullOrWhiteSpace(patch) || patch.Any(char.IsControl)))
                throw new InvalidDataException("Invalid server client version metadata.");
            ValidateLanguages(preset.ClientLanguages);
            if (preset.DownloadSource is { } download)
            {
                if (download.Kind is not ("http" or "alist" or "github")) throw new InvalidDataException("Invalid client distribution transport.");
                if (string.IsNullOrWhiteSpace(download.BaseUrl)) throw new InvalidDataException("Missing client distribution URL.");
                _ = SourceAddress.BaseUrl(download.BaseUrl);
                _ = SourceAddress.RelativePath(download.CatalogPath);
                if (string.IsNullOrWhiteSpace(download.PackageId)) throw new InvalidDataException("Missing client package id.");
                Definitions.PluginCatalog.ValidateId("server catalog", "downloadSource.packageId", download.PackageId);
            }
            ServerStore.Validate(server with { Endpoint = preset.Endpoint });
        }
        if (catalog.Instances.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Instances.Count) throw new InvalidDataException("Duplicate server instance preset.");
        try { ServerMetadataPolicy.Validate(catalog.Presentation); ServerMetadataPolicy.Validate(catalog.ClientPolicy); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid server display or plugin-source metadata.", error); }
        foreach (var field in catalog.RegistrationFields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Key) || !field.Key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ||
                field.Key is "username" or "password" || field.Type is not ("text" or "password" or "email" or "number" or "select" or "checkbox") ||
                field.MaxLength < 1 || field.Label is null || !field.Label.ContainsKey("en") || field.Label.Values.Any(v => v is null) ||
                field.Choices is null || field.Choices.Any(v => v is null) || field.Type == "select" && field.Choices.Count == 0)
                throw new InvalidDataException("Unsupported registration field.");
        }
        if (catalog.RegistrationFields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != catalog.RegistrationFields.Count) throw new InvalidDataException("Duplicate registration field.");
        if (catalog.HostPolicy is not (null or "auto" or "ip" or "domain") || catalog.LauncherLogin is not (null or "required" or "optional" or "none" or "game")) throw new InvalidDataException("Invalid server policy.");
        if (catalog.SecureUrl is { } secure && (!Uri.TryCreate(secure, UriKind.Absolute, out var secureUri) || secureUri.Scheme != Uri.UriSchemeHttps
            || secureUri.UserInfo.Length > 0 || secureUri.Query.Length > 0 || secureUri.Fragment.Length > 0)) throw new InvalidDataException("Invalid secure server address.");
        if (catalog.Region is not null && (catalog.Region.Length != 2 || !catalog.Region.All(char.IsAsciiLetterUpper))) throw new InvalidDataException("Invalid deployment region.");
        ValidateLanguages(catalog.ClientLanguages);
        if (catalog.ProfileFields is { } profileFields && (profileFields.Any(string.IsNullOrWhiteSpace) || profileFields.Distinct(StringComparer.Ordinal).Count() != profileFields.Count)) throw new InvalidDataException("Invalid profile fields.");
        // Merge only after validating the response; omitted booleans must not become remote policy changes.
        catalog = catalog with { Presentation = ServerCatalogPresentation.Merge(server.Presentation, catalog.Presentation, response) };
        lock (_gate)
        {
            LauncherCompatibility.ValidateMinimum(catalog.MinimumLauncherVersion);
            ValidatePolicy(catalog.ClientPolicy);
            if (_catalogs.GetValueOrDefault(key) != snapshot) throw new InvalidOperationException("The server changed during discovery. Refresh it again.");
            if (revision >= snapshot.Applied)
            {
                snapshot.Applied = revision;
                snapshot.Value = catalog;
                _policies[apiUrl] = (catalog.HostPolicy, catalog.Region, catalog.MinimumLauncherVersion, catalog.ClientPolicy);
                if (catalog.SecureUrl is { } secureUrl) _secureUrls[apiUrl] = secureUrl;
                else _secureUrls.Remove(apiUrl);
            }
        }
        if (!allowIncompatible) LauncherCompatibility.Require(catalog.MinimumLauncherVersion, server.Name, _launcherVersion);
        return catalog;
    }

    /// <summary>Reads opted-in login form credentials for the selected server endpoint, independently of its refresh sessions.</summary>
    public RememberedLogin? RememberedLogin(ServerDefinition server) => server.ApiUrl is null ? null : _loginStore?.Read(server.Id, server.ApiUrl);

    /// <summary>Authenticates without retaining the password; no bearer is returned to the frontend.</summary>
    public Task<AccountProfile> LoginAsync(ServerDefinition server, string username, string password, CancellationToken ct = default) =>
        LoginAsync(server, username, password, false, ct);

    /// <summary>Authenticates and optionally retains a successful password in protected storage; no bearer is returned to the frontend.</summary>
    public async Task<AccountProfile> LoginAsync(ServerDefinition server, string username, string password, bool rememberPassword, CancellationToken ct = default)
    {
        ValidateCredentials(username, password);
        if (rememberPassword && _loginStore is null) throw new InvalidOperationException("Protected password storage is unavailable.");
        ServerSessions owner;
        long revision;
        Dictionary<string, Guid> previousIdentities;
        lock (_gate)
        {
            owner = Owner(server, create: true)!;
            Prune(owner);
            revision = owner.Revision;
            previousIdentities = owner.Accounts.ToDictionary(pair => pair.Key, pair => pair.Value.Identity, StringComparer.Ordinal);
            owner.PendingLogins++;
        }
        try
        {
            _ = await CatalogAsync(server, ct).ConfigureAwait(false);
            var response = await SendAsync<AuthResponse>(server, HttpMethod.Post, "v1/auth/login", new { username, password, launcherVersion = _launcherVersion }, null, ct).ConfigureAwait(false);
            ValidatePolicy(response.ClientPolicy);
            LauncherCompatibility.Require(response.MinimumLauncherVersion, server.Name, _launcherVersion);
            var accessToken = response.AccessToken;
            var accessExpiresAt = response.AccessExpiresAt;
            if (response.Account is null || accessToken is null || accessExpiresAt is null || !ValidToken(accessToken) || accessExpiresAt <= _clock.GetUtcNow() || accessExpiresAt > _clock.GetUtcNow().AddDays(1))
                throw new InvalidDataException("Invalid account session response.");
            ValidateRefresh(response.RefreshToken, response.RefreshExpiresAt, accessExpiresAt.Value);
            ValidateProfile(response.Account);
            lock (_gate)
            {
                if (_sessions.GetValueOrDefault(server.Id) != owner)
                    throw new InvalidOperationException("The server changed while signing in. Sign in again.");
                // Expired credentials are pruned even if their server has not been visited.
                foreach (var group in _sessions.Values) Prune(group);
                Guid? previousIdentity = previousIdentities.TryGetValue(response.Account.Username, out var identity) ? identity : null;
                if (owner.Accounts.GetValueOrDefault(response.Account.Username)?.Identity != previousIdentity)
                    throw new InvalidOperationException("This account session changed while signing in. Sign in again.");
                _loginStore?.Save(server.Id, owner.ApiUrl, response.Account.Username, password, rememberPassword);
                owner.Accounts[response.Account.Username] = new Session(accessToken, accessExpiresAt.Value, response.RefreshToken, response.RefreshExpiresAt, response.Account)
                { MinimumLauncherVersion = response.MinimumLauncherVersion, ClientPolicy = response.ClientPolicy };
                if (owner.Revision == revision)
                {
                    owner.Selected = response.Account.Username;
                    owner.Revision++;
                }
                else if (owner.Selected == response.Account.Username) owner.Revision++;
            }
            PersistOwner(server.Id, owner);
            return response.Account;
        }
        finally { lock (_gate) owner.PendingLogins--; }
    }

    /// <summary>Registers with the server's current form schema, validating every extension field before submission.</summary>
    public async Task RegisterAsync(ServerDefinition server, string username, string password, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        ValidateCredentials(username, password);
        var catalog = await CatalogAsync(server, ct).ConfigureAwait(false);
        if (!catalog.RegistrationEnabled) throw new InvalidOperationException("Registration is disabled by this server.");
        if (fields.Keys.Any(k => !catalog.RegistrationFields.Any(f => f.Key == k))) throw new ArgumentException("Unknown registration field.");
        foreach (var field in catalog.RegistrationFields)
        {
            var value = fields.GetValueOrDefault(field.Key) ?? "";
            if (value.Length > field.MaxLength || field.Required && string.IsNullOrWhiteSpace(value) || field.Required && field.Type == "checkbox" && value != "true" ||
                value.Length != 0 && field.Type == "select" && !field.Choices.Contains(value, StringComparer.Ordinal) ||
                value.Length != 0 && field.Type == "checkbox" && value is not ("true" or "false")) throw new ArgumentException("Invalid registration field: " + field.Key);
        }
        _ = await SendAsync<JsonElement>(server, HttpMethod.Post, "v1/auth/register", new { username, password, fields, launcherVersion = _launcherVersion }, null, ct).ConfigureAwait(false);
    }

    /// <summary>Returns the current public profile only while the exact server session remains valid.</summary>
    public AccountProfile? Current(ServerDefinition server) => Find(server)?.Session.Profile;

    /// <summary>Captures the current server account before launch preparation waits for catalogs, plugins or client metadata.</summary>
    public AccountLaunchSelection CaptureLaunchSelection(string serverId)
    {
        lock (_gate)
        {
            var owner = _sessions.GetValueOrDefault(serverId);
            if (owner is not null) Prune(owner);
            var session = owner?.Selected is { } username ? owner.Accounts.GetValueOrDefault(username) : null;
            return new(this, serverId, owner, owner?.Revision ?? 0, session?.Identity);
        }
    }

    /// <summary>Rejects a changed account, endpoint or session before a prepared launch performs authenticated work.</summary>
    public void RequireLaunchSelection(ServerDefinition server, AccountLaunchSelection selection) => _ = FindForLaunch(server, selection);

    private Selection? FindForLaunch(ServerDefinition server, AccountLaunchSelection? expected)
    {
        lock (_gate)
        {
            var current = Find(server);
            var owner = Owner(server);
            if (expected is not null && (expected.Client != this || expected.ServerId != server.Id || expected.Owner != owner ||
                expected.Revision != (owner?.Revision ?? 0) || expected.Identity != current?.Session.Identity))
                throw new InvalidOperationException("The selected account changed during launch. Start the game again.");
            return current;
        }
    }

    /// <summary>Returns all valid sessions and their active selection without exposing tokens.</summary>
    public AccountSessionList Accounts(ServerDefinition server)
    {
        lock (_gate)
        {
            var owner = Owner(server);
            if (owner is null) return new(null, []);
            Prune(owner);
            return new(owner.Selected is { } name ? owner.Accounts.GetValueOrDefault(name)?.Profile : null,
                owner.Accounts.Values.Select(session => new AccountSessionInfo(session.Profile, session.ExpiresAt)
                { RefreshExpiresAt = session.RefreshExpiresAt }).ToArray());
        }
    }

    /// <summary>Renews the selected account before its access token expires, when the server issued a refresh token.</summary>
    public async Task<AccountProfile?> RefreshIfNeededAsync(ServerDefinition server, CancellationToken ct = default)
    {
        var selection = Find(server);
        if (selection is null || selection.Session.RefreshToken is null || selection.Session.ExpiresAt > _clock.GetUtcNow().AddMinutes(2)) return selection?.Session.Profile;
        await RenewAsync(server, selection, ct).ConfigureAwait(false);
        return Current(server);
    }

    /// <summary>Selects an existing valid session belonging to this exact server endpoint.</summary>
    public AccountSessionList Select(ServerDefinition server, string username)
    {
        lock (_gate)
        {
            var owner = Owner(server);
            if (owner is not null) Prune(owner);
            if (owner is null || !owner.Accounts.ContainsKey(username))
                throw new InvalidOperationException("This account session expired. Sign in again.");
            if (owner.Selected != username) { owner.Selected = username; owner.Revision++; }
            var result = Accounts(server);
            PersistOwner(server.Id, owner);
            return result;
        }
    }

    /// <summary>Refreshes account statistics using the current session, or returns null when signed out.</summary>
    public async Task<AccountProfile?> RefreshAsync(ServerDefinition server, CancellationToken ct = default)
    {
        await RefreshIfNeededAsync(server, ct).ConfigureAwait(false);
        var selection = Find(server); if (selection is null) return null;
        var session = selection.Session;
        var profile = await SendWithSessionRetryAsync<AccountProfile>(server, HttpMethod.Get, "v1/account", null, selection, ct).ConfigureAwait(false);
        ValidateProfile(profile);
        if (profile.Username != session.Profile.Username) throw new InvalidDataException("The account profile changed identity.");
        AccountProfile? current;
        lock (_gate)
        {
            if (_sessions.GetValueOrDefault(server.Id) == selection.Owner && selection.Owner.Accounts.GetValueOrDefault(profile.Username)?.Identity == session.Identity)
            {
                var currentSession = selection.Owner.Accounts.GetValueOrDefault(profile.Username);
                if (currentSession is not null) currentSession.Profile = profile;
            }
            current = Current(server);
        }
        PersistOwner(server.Id, selection.Owner);
        return current;
    }

    /// <summary>Requests the wire dialect while attaching optional installed version labels as descriptive metadata.</summary>
    public Task<LaunchCredentials?> CredentialsAsync(ServerDefinition server, BuildDefinition build, ClientVersionMetadata metadata, bool required, CancellationToken ct = default) =>
        CredentialsCoreAsync(server, (string.IsNullOrWhiteSpace(build.Region) ? build.Branch : build.Region).ToLowerInvariant()
            + "-" + build.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), required, metadata.ReleaseVersion, metadata.PatchVersion, null, ct, build.ProtocolVersion);

    /// <summary>Attaches distribution and language context to the same ticket request without an additional API call.</summary>
    public Task<LaunchCredentials?> CredentialsAsync(ServerDefinition server, BuildDefinition build, ClientVersionMetadata metadata,
        ClientLanguageMetadata languages, bool required, CancellationToken ct = default) =>
        CredentialsCoreAsync(server, (string.IsNullOrWhiteSpace(build.Region) ? build.Branch : build.Region).ToLowerInvariant()
            + "-" + build.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), required,
            metadata.ReleaseVersion, metadata.PatchVersion, languages, ct, build.ProtocolVersion);

    /// <summary>Requests a launch ticket only for the exact account captured before asynchronous launch preparation.</summary>
    public Task<LaunchCredentials?> CredentialsAsync(ServerDefinition server, BuildDefinition build, ClientVersionMetadata metadata,
        ClientLanguageMetadata languages, bool required, AccountLaunchSelection selection, CancellationToken ct = default) =>
        CredentialsCoreAsync(server, (string.IsNullOrWhiteSpace(build.Region) ? build.Branch : build.Region).ToLowerInvariant()
            + "-" + build.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), required,
            metadata.ReleaseVersion, metadata.PatchVersion, languages, ct, build.ProtocolVersion, selection);

    /// <summary>Requests issuer-bound native selection for an audited module without changing ordinary launch requests.</summary>
    public Task<LaunchCredentials?> SelectionCredentialsAsync(ServerDefinition server, BuildDefinition build, ClientVersionMetadata metadata,
        ClientLanguageMetadata languages, AccountLaunchSelection selection, CancellationToken ct = default) =>
        CredentialsCoreAsync(server, WireBuild(build), true, metadata.ReleaseVersion, metadata.PatchVersion,
            languages, ct, build.ProtocolVersion, selection, serverSelection: true);

    /// <summary>Obtains a fresh ticket for the wire dialect; an arbitrary frontend account name cannot satisfy required login.</summary>
    public Task<LaunchCredentials?> CredentialsAsync(ServerDefinition server, string build, bool required, CancellationToken ct = default) =>
        CredentialsCoreAsync(server, build, required, null, null, null, ct);

    private async Task<LaunchCredentials?> CredentialsCoreAsync(ServerDefinition server, string build, bool required,
        string? releaseVersion, string? patchVersion, ClientLanguageMetadata? languages, CancellationToken ct, int? protocolVersion = null,
        AccountLaunchSelection? expectedSelection = null, bool serverSelection = false)
    {
        var selection = FindForLaunch(server, expectedSelection);
        if (selection is null) { if (required) throw new InvalidOperationException("Sign in to this server before starting this client."); return null; }
        var session = selection.Session;
        if (session.RefreshToken is not null && session.ExpiresAt <= _clock.GetUtcNow().AddMinutes(2))
            await RenewAsync(server, selection, ct).ConfigureAwait(false);
        RequireSelection(server, selection);
        LauncherCompatibility.Require(session.MinimumLauncherVersion, server.Name, _launcherVersion);
        var body = LaunchBody(build, releaseVersion, patchVersion, languages, protocolVersion);
        if (serverSelection) body["serverSelection"] = true;
        var credentials = await SendWithSessionRetryAsync<LaunchCredentials>(server, HttpMethod.Post, "v1/launch", body, selection, ct,
            maximumResponseBytes: serverSelection ? 8 * 1024 * 1024 : null).ConfigureAwait(false);
        if (credentials.Account != session.Profile.Username) throw new InvalidDataException("The server issued a launch ticket for a different account. Sign out and sign in again.");
        if (string.IsNullOrEmpty(credentials.Ticket) || credentials.Ticket.Length > 4096 || credentials.Ticket.Any(char.IsControl))
            throw new InvalidDataException("The server returned an empty or malformed launch ticket. Try again or contact the server administrator.");
        if (serverSelection)
        {
            if (credentials.ServerSelection is not { } grant) throw new InvalidDataException("The server omitted native selection admission.");
            AutomaticServerSelection.ValidateGrant(grant, build, EffectiveApiUrl(server));
            if (grant.ExpiresAtUtc > credentials.ExpiresAt) throw new InvalidDataException("Selection admission outlives its launch ticket.");
        }
        if (credentials.Messenger is { } messenger && (!SessionMessenger.IsValidUrl(messenger.Url) || !SessionMessenger.IsValidTicket(messenger.Ticket)))
            throw new InvalidDataException("The server returned a malformed messenger sign-in. Try again or contact the server administrator.");
        // Nirinsou Link is optional: a malformed grant leaves this launch without it instead of refusing the game.
        if (credentials.Companion is { } companion && !IsValidCompanion(companion)) credentials = credentials with { Companion = null };
        // The server redeems this freshly requested ticket; the player's clock cannot establish its expiry.
        RequireSelection(server, selection);
        return credentials;
    }

    /// <summary>
    /// Requests a fresh one-time Nirinsou Link ticket for a launch the selected account holds; null when the server no
    /// longer knows the launch or does not run Nirinsou Link.
    /// </summary>
    public async Task<LaunchCompanion?> RenewCompanionAsync(ServerDefinition server, string launchId, CancellationToken ct = default)
    {
        var selection = FindForLaunch(server, null);
        if (selection is null) return null;
        try
        {
            var grant = await SendWithSessionRetryAsync<LaunchCompanion>(server, HttpMethod.Post, "v1/companion/tickets",
                new Dictionary<string, object> { ["launchId"] = launchId, ["launcherVersion"] = _launcherVersion }, selection, ct).ConfigureAwait(false);
            return grant.LaunchId == launchId && IsValidCompanion(grant) ? grant : null;
        }
        catch (ServerFailureException error) when (error.Code is "launch_not_found" or "companion_unavailable") { return null; }
    }

    /// <summary>The launcher version every request reports.</summary>
    public string LauncherVersion => _launcherVersion;

    /// <summary>
    /// The configured API address unless Use HTTPS is enabled; that preference permits the advertised secure
    /// address or upgrades an entered HTTP address while retaining its path and nondefault port.
    /// </summary>
    public string EffectiveApiUrl(ServerDefinition server)
    {
        var apiUrl = server.ApiUrl ?? throw new InvalidOperationException("Configure this server's launcher API address first.");
        lock (_gate) if (server.UseHttps && _secureUrls.TryGetValue(apiUrl, out var secure)) return ServerStore.ApiBase(secure).AbsoluteUri;
        var entered = ServerStore.ApiBase(apiUrl);
        return server.UseHttps && entered.Scheme == Uri.UriSchemeHttp ? new UriBuilder(entered) { Scheme = Uri.UriSchemeHttps, Port = entered.IsDefaultPort ? -1 : entered.Port }.Uri.AbsoluteUri : entered.AbsoluteUri;
    }

    /// <summary>
    /// Opens a launch for a build whose own login screen signs in: the same /v1/launch the launcher sends after a
    /// sign-in, without the bearer. The game binds it with the player's sign-in.
    /// </summary>
    public async Task<GameLoginLaunch> OpenGameLoginAsync(ServerDefinition server, BuildDefinition build, ClientVersionMetadata metadata,
        ClientLanguageMetadata languages, CancellationToken ct = default)
    {
        await DiscoverRoutingAsync(server, ct).ConfigureAwait(false);
        var body = LaunchBody(WireBuild(build), metadata.ReleaseVersion, metadata.PatchVersion, languages, build.ProtocolVersion);
        var launch = await SendAsync<GameLoginLaunch>(server, HttpMethod.Post, "v1/launch", body, null, ct).ConfigureAwait(false);
        if (launch.LaunchId is not { Length: 64 } id || !id.All(char.IsAsciiHexDigitLower) || launch.LaunchKey is not { Length: 64 } key || !key.All(char.IsAsciiHexDigitLower))
            throw new InvalidDataException("The server returned a malformed in-game sign-in launch.");
        return launch;
    }

    /// <summary>Gets the companion grant of an in-game sign-in launch, or null until the game has signed in.</summary>
    public async Task<LaunchCompanion?> GameLoginCompanionAsync(ServerDefinition server, GameLoginLaunch launch, CancellationToken ct = default)
    {
        try
        {
            var grant = await SendAsync<LaunchCompanion>(server, HttpMethod.Post, "v1/launch/companion",
                new Dictionary<string, object> { ["launchId"] = launch.LaunchId, ["launchKey"] = launch.LaunchKey, ["launcherVersion"] = _launcherVersion }, null, ct).ConfigureAwait(false);
            return IsValidCompanion(grant) ? grant : null;
        }
        catch (ServerFailureException error) when (error.Code is "launch_not_found" or "launch_not_bound" or "companion_unavailable") { return null; }
    }

    // The /v1/launch body every launch sends; an in-game sign-in sends the same one from the game.
    private Dictionary<string, object> LaunchBody(string build, string? releaseVersion, string? patchVersion, ClientLanguageMetadata? languages, int? protocolVersion)
    {
        var body = new Dictionary<string, object> { ["build"] = build, ["launcherVersion"] = _launcherVersion };
        if (protocolVersion is { } protocol) body["protocol_version"] = protocol;
        if (VersionLabel(releaseVersion) is { } release) body["release_version"] = release;
        if (VersionLabel(patchVersion) is { } patch) body["patch_version"] = patch;
        if (languages is not null)
        {
            foreach (var (key, value) in new[] { ("launcher_language", languages.LauncherLanguage),
                ("native_language", languages.NativeLanguage), ("client_language", languages.ClientLanguage) })
            {
                if (value.Length == 0) continue;
                if (value is not ("zh-CN" or "zh-TW" or "ko" or "en" or "id" or "vi")) throw new ArgumentException("Unsupported launch language.");
                body[key] = value;
            }
        }
        return body;
    }

    /// <summary>The wire build id the server's presets use, such as id-400.</summary>
    public static string WireBuild(BuildDefinition build) =>
        (string.IsNullOrWhiteSpace(build.Region) ? build.Branch : build.Region).ToLowerInvariant()
        + "-" + build.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsValidCompanion(LaunchCompanion grant) =>
        grant.Url is { Length: > 0 and <= 2048 } && Uri.TryCreate(grant.Url, UriKind.Absolute, out var url) && url.Scheme is "ws" or "wss"
        && url.Host.Length > 0 && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0
        && grant.Ticket is { Length: > 0 and <= 4096 } && grant.Ticket.All(c => c is > (char)0x20 and < (char)0x7f)
        && grant.LaunchId is { Length: > 0 and <= 128 } && grant.LaunchId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private void RequireSelection(ServerDefinition server, Selection selection)
    {
        lock (_gate)
        {
            var current = Find(server);
            if (current is null || current.Owner != selection.Owner || current.Revision != selection.Revision || current.Session.Identity != selection.Session.Identity)
                throw new InvalidOperationException("The selected account changed during launch. Start the game again.");
        }
    }

    private static string? VersionLabel(string? value) => string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(char.IsControl) ? null : value.Trim();

    /// <summary>Revokes only the specified account (or the selected account), retaining other server sessions.</summary>
    public async Task LogoutAsync(ServerDefinition server, string? username = null, CancellationToken ct = default)
    {
        Session? session;
        ServerSessions? owner;
        string? name;
        lock (_gate)
        {
            owner = Owner(server);
            name = username ?? owner?.Selected;
            session = name is null ? null : owner?.Accounts.GetValueOrDefault(name);
        }
        if (session is null || owner is null || name is null) return;
        // Finish any atomic token rotation before revoking its resulting session.
        await session.RefreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_sessions.GetValueOrDefault(server.Id) != owner || owner.Accounts.GetValueOrDefault(name) != session) return;
                owner.Accounts.Remove(name);
                if (owner.Selected == name) { owner.Selected = null; owner.Revision++; }
            }
            PersistOwner(server.Id, owner);
            await DiscoverRoutingAsync(server, ct, allowIncompatible: true).ConfigureAwait(false);
            _ = await SendAsync<JsonElement>(server, HttpMethod.Post, "v1/auth/logout", new { refreshToken = session.RefreshToken }, string.IsNullOrEmpty(session.Token) ? null : session.Token, ct).ConfigureAwait(false);
        }
        finally { session.RefreshGate.Release(); }
    }

    /// <summary>Drops every session and fences in-flight logins for an edited or removed server.</summary>
    public void Forget(string serverId)
    {
        lock (_gate)
        {
            _sessions.Remove(serverId);
            foreach (var key in _catalogs.Keys.Where(key => key.Id == serverId).ToArray()) _catalogs.Remove(key);
            _sessionStore?.Remove(serverId);
            _loginStore?.Remove(serverId);
        }
    }

    private ServerSessions? Owner(ServerDefinition server, bool create = false)
    {
        if (_sessions.TryGetValue(server.Id, out var owner))
        {
            if (owner.ApiUrl == server.ApiUrl) return owner;
            _sessions.Remove(server.Id);
            _sessionStore?.Remove(server.Id);
        }
        if (!create) return null;
        // Retain in-flight owners so a delayed authentication cannot outlive its scope.
        foreach (var pair in _sessions.ToArray())
        {
            Prune(pair.Value);
            if (pair.Value.Accounts.Count == 0 && pair.Value.PendingLogins == 0) _sessions.Remove(pair.Key);
        }
        owner = new ServerSessions(server.ApiUrl ?? throw new InvalidOperationException("Configure this server's launcher API address first."));
        _sessions.Add(server.Id, owner);
        return owner;
    }

    private void Prune(ServerSessions owner)
    {
        var now = _clock.GetUtcNow();
        foreach (var name in owner.Accounts.Where(pair => pair.Value.ExpiresAt <= now && (pair.Value.RefreshExpiresAt is not { } refreshExpiry || refreshExpiry <= now)).Select(pair => pair.Key).ToArray())
        {
            owner.Accounts.Remove(name);
            if (owner.Selected == name) { owner.Selected = null; owner.Revision++; }
        }
    }

    private Selection? Find(ServerDefinition server)
    {
        lock (_gate)
        {
            var owner = Owner(server);
            if (owner is null) return null;
            Prune(owner);
            return owner.Selected is { } name && owner.Accounts.TryGetValue(name, out var session) ? new(owner, session, owner.Revision) : null;
        }
    }

    private void RejectToken(ServerDefinition server, string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        ServerSessions? owner;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(server.Id, out owner) || owner.ApiUrl != server.ApiUrl) return;
            foreach (var name in owner.Accounts.Where(pair => pair.Value.Token == token).Select(pair => pair.Key).ToArray())
            {
                owner.Accounts.Remove(name);
                if (owner.Selected == name) { owner.Selected = null; owner.Revision++; }
            }
        }
        PersistOwner(server.Id, owner);
    }

    private async Task RenewAsync(ServerDefinition server, Selection selection, CancellationToken ct, string? rejectedToken = null)
    {
        var session = selection.Session;
        await session.RefreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Routing is process-local; restored credentials must not precede public discovery.
            // A rejected catalog is not evidence that the refresh credential has expired.
            await DiscoverRoutingAsync(server, ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (_sessions.GetValueOrDefault(server.Id) != selection.Owner || selection.Owner.Accounts.GetValueOrDefault(session.Profile.Username)?.Identity != session.Identity)
                    throw new InvalidOperationException("The selected account changed while renewing its session. Sign in again.");
                if (session.RefreshToken is null || !session.InvalidPolicy && (rejectedToken is null ? session.ExpiresAt > _clock.GetUtcNow().AddMinutes(2) : session.Token != rejectedToken)) return;
                if (session.RefreshExpiresAt is { } refreshExpiry && refreshExpiry <= _clock.GetUtcNow())
                {
                    RemoveSession(server, session);
                    throw new UnauthorizedAccessException("Sign-in expired. Sign in again.");
                }
            }

            AuthResponse? response;
            try
            {
                response = await SendAsync<AuthResponse>(server, HttpMethod.Post, "v1/auth/refresh", new { refreshToken = session.RefreshToken, launcherVersion = _launcherVersion }, null, ct).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                lock (_gate) RemoveSession(server, session);
                throw new UnauthorizedAccessException("Sign-in expired. Sign in again.");
            }

            var accessToken = response.AccessToken;
            var accessExpiresAt = response.AccessExpiresAt;
            if (accessToken is null || accessExpiresAt is null || !ValidToken(accessToken) || accessExpiresAt <= _clock.GetUtcNow() || accessExpiresAt > _clock.GetUtcNow().AddDays(1))
                throw new InvalidDataException("Invalid refreshed account session response.");
            ValidateRefresh(response.RefreshToken, response.RefreshExpiresAt, accessExpiresAt.Value);
            if (response.Account is not null)
            {
                ValidateProfile(response.Account);
                if (response.Account.Username != session.Profile.Username) throw new InvalidDataException("The refreshed account changed identity.");
            }
            lock (_gate)
            {
                if (_sessions.GetValueOrDefault(server.Id) != selection.Owner || selection.Owner.Accounts.GetValueOrDefault(session.Profile.Username)?.Identity != session.Identity)
                    throw new InvalidOperationException("The selected account changed while renewing its session. Sign in again.");
                session.Token = accessToken;
                session.ExpiresAt = accessExpiresAt.Value;
                // Preserve a valid rotated credential even when extension metadata is malformed.
                session.InvalidPolicy = true;
                if (response.RefreshToken is not null) session.RefreshToken = response.RefreshToken;
                if (response.RefreshExpiresAt is not null) session.RefreshExpiresAt = response.RefreshExpiresAt;
                if (response.Account is not null) session.Profile = response.Account;
            }
            PersistOwner(server.Id, selection.Owner);
            LauncherCompatibility.ValidateMinimum(response.MinimumLauncherVersion);
            ValidatePolicy(response.ClientPolicy);
            lock (_gate)
            {
                session.MinimumLauncherVersion = response.MinimumLauncherVersion;
                session.ClientPolicy = response.ClientPolicy;
                session.InvalidPolicy = false;
            }
            LauncherCompatibility.Require(session.MinimumLauncherVersion, server.Name, _launcherVersion);
        }
        finally { session.RefreshGate.Release(); }
    }

    private async Task DiscoverRoutingAsync(ServerDefinition server, CancellationToken ct, bool allowIncompatible = false)
    {
        lock (_gate) if (_policies.ContainsKey(server.ApiUrl!)) return;
        _ = await CatalogCoreAsync(server, ct, allowIncompatible).ConfigureAwait(false);
    }

    private async Task<T> SendWithSessionRetryAsync<T>(ServerDefinition server, HttpMethod method, string path, object? body, Selection selection, CancellationToken ct,
        long? maximumResponseBytes = null)
    {
        if (selection.Session.InvalidPolicy) throw new InvalidDataException("Invalid account authentication policy.");
        LauncherCompatibility.Require(selection.Session.MinimumLauncherVersion, server.Name, _launcherVersion);
        string token;
        lock (_gate) token = selection.Session.Token;
        try { return await SendAsync<T>(server, method, path, body, token, ct, retainUnauthorized: true, maximumResponseBytes: maximumResponseBytes).ConfigureAwait(false); }
        catch (UnauthorizedAccessException)
        {
            if (selection.Session.RefreshToken is null)
            {
                RejectToken(server, selection.Session.Token);
                throw;
            }
            await RenewAsync(server, selection, ct, token).ConfigureAwait(false);
            lock (_gate)
            {
                var current = Find(server);
                if (current is null || current.Owner != selection.Owner || current.Session.Identity != selection.Session.Identity || current.Revision != selection.Revision)
                    throw new InvalidOperationException("The selected account changed during the request. Try again.");
                token = selection.Session.Token;
            }
            return await SendAsync<T>(server, method, path, body, token, ct, maximumResponseBytes: maximumResponseBytes).ConfigureAwait(false);
        }
    }

    private void RemoveSession(ServerDefinition server, Session session)
    {
        ServerSessions? owner;
        lock (_gate)
        {
            if (_sessions.GetValueOrDefault(server.Id) is not { } found || found.Accounts.GetValueOrDefault(session.Profile.Username)?.Identity != session.Identity) return;
            owner = found;
            owner.Accounts.Remove(session.Profile.Username);
            if (owner.Selected == session.Profile.Username) { owner.Selected = null; owner.Revision++; }
        }
        PersistOwner(server.Id, owner);
    }

    private async Task<T> SendAsync<T>(ServerDefinition server, HttpMethod method, string path, object? body, string? token, CancellationToken ct, string? discoveryPolicy = null, bool retainUnauthorized = false, long? maximumResponseBytes = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await SendOnceAsync<T>(server, method, path, body, token, discoveryPolicy, retainUnauthorized, maximumResponseBytes, ct).ConfigureAwait(false); }
            catch (ServerFailureException error) when (attempt == 0 && method == HttpMethod.Get && !ct.IsCancellationRequested
                && (error.Code is "network_error" or "network_timeout"
                    || error.Code == "http_error" && error.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
            {
                // Only reads are replayed. Ticket issuance, account changes and admission POSTs
                // can have committed before a transport error and must never be duplicated here.
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<T> SendOnceAsync<T>(ServerDefinition server, HttpMethod method, string path, object? body, string? token, string? discoveryPolicy, bool retainUnauthorized, long? maximumResponseBytes, CancellationToken ct)
    {
        try { return await SendCoreAsync<T>(server, method, path, body, token, discoveryPolicy, retainUnauthorized, maximumResponseBytes, ct).ConfigureAwait(false); }
        catch (ServerFailureException) { throw; }
        catch (HttpRequestException error) { throw new ServerFailureException("network_error", error.StatusCode, error.HttpRequestError.ToString()); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServerFailureException("network_timeout", transportCode: "timeout"); }
    }

    private async Task<T> SendCoreAsync<T>(ServerDefinition server, HttpMethod method, string path, object? body, string? token, string? discoveryPolicy, bool retainUnauthorized, long? maximumResponseBytes, CancellationToken ct)
    {
        // Discovery reads the address as entered; everything after it uses the secure address when there is one.
        var baseUri = path == "v1/catalog"
            ? ServerStore.ApiBase(server.ApiUrl ?? throw new InvalidOperationException("Configure this server's launcher API address first."))
            : new Uri(EffectiveApiUrl(server));
        (string? Policy, string? Region, string? Minimum, ClientPolicy Plugins) policy;
        lock (_gate) policy = _policies.GetValueOrDefault(server.ApiUrl!);
        if (path is not ("v1/catalog" or "v1/auth/logout")) LauncherCompatibility.Require(policy.Minimum, server.Name, _launcherVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20)); ct = timeout.Token;
        var uri = await _network.RequestUriAsync(new Uri(baseUri, path), server, discoveryPolicy ?? policy.Policy, policy.Region, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-FantasyTale-Launcher-Version", _launcherVersion);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (maximumResponseBytes is { } responseLimit)
            await response.Content.LoadIntoBufferAsync(responseLimit, ct).ConfigureAwait(false);
        if ((int)response.StatusCode == 426)
        {
            await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                if (error.RootElement.GetProperty("error").GetString() != "launcher_update_required") throw new InvalidDataException("Invalid launcher version rejection.");
                var minimum = error.RootElement.GetProperty("minimumLauncherVersion").GetString();
                if (string.IsNullOrEmpty(minimum)) throw new InvalidDataException("Missing minimum launcher version.");
                LauncherCompatibility.ValidateMinimum(minimum);
                throw new LauncherUpgradeRequiredException(minimum, _launcherVersion, server.Name);
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException || error is InvalidOperationException and not LauncherUpgradeRequiredException)
            { throw new InvalidDataException("Malformed launcher version rejection.", error); }
        }
        if (!response.IsSuccessStatusCode)
        {
            await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                var root = document.RootElement;
                string? Text(string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                DateTimeOffset? Time(string key) => DateTimeOffset.TryParse(Text(key), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var value) ? value : null;
                var code = Text("error");
                if (code is "wrong_password" or "account_not_found" or "account_banned" or "server_full" or "server_maintenance" or "server_not_ready" or "launch_not_found" or "launch_not_bound" or "companion_unavailable" or "not_whitelisted" or "password_too_short" or "invalid_credentials"
                    or "admission_closed" or "server_selection_required" or "server_selection_unavailable")
                    throw new ServerFailureException(code, response.StatusCode, reason: Text("reason"),
                        maintenanceStart: Time("maintenanceStart"), maintenanceEnd: Time("maintenanceEnd"), maintenanceDescription: Text("maintenanceDescription"));
            }
            catch (JsonException) { }
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized) { if (!retainUnauthorized) RejectToken(server, token); throw new UnauthorizedAccessException("Sign-in expired or the account credentials were rejected."); }
        if (!response.IsSuccessStatusCode) throw new ServerFailureException("http_error", response.StatusCode);
        await using var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try { return await JsonSerializer.DeserializeAsync<T>(content, Json, ct).ConfigureAwait(false) ?? throw new InvalidDataException("Empty API response."); }
        catch (JsonException) { throw new InvalidDataException("Malformed launcher API response."); }
    }

    private static bool ValidToken(string token) => token.Length is > 0 and <= 4096 && !token.Any(char.IsControl);

    /// <summary>
    /// The client policy launches to this server use: the signed-in account's policy from its last sign-in or
    /// refresh, otherwise the live catalog's, otherwise the one saved with the server profile.
    /// </summary>
    public ClientPolicy ClientPolicy(ServerDefinition server)
    {
        lock (_gate)
        {
            if (server.ApiUrl is null) return server.ClientPolicy;
            var owner = Owner(server, create: false);
            var session = owner?.Selected is { } selected ? owner.Accounts.GetValueOrDefault(selected) : null;
            return session?.ClientPolicy ?? (_policies.TryGetValue(server.ApiUrl, out var policy) ? policy.Plugins : server.ClientPolicy);
        }
    }

    private static void ValidatePolicy(ClientPolicy? policy)
    {
        try { ServerMetadataPolicy.Validate(policy!); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid server client policy: " + error.Message, error); }
    }

    private void ValidateRefresh(string? token, DateTimeOffset? expiresAt, DateTimeOffset accessExpiresAt)
    {
        if (token is null || expiresAt is null) throw new InvalidDataException("Refresh token and expiry are required.");
        if (!ValidToken(token) || expiresAt <= accessExpiresAt || expiresAt > _clock.GetUtcNow().AddDays(365))
            throw new InvalidDataException("Invalid refresh token response.");
    }

    private static void ValidateLanguages(IReadOnlyList<string>? languages)
    {
        if (languages is not null && (languages.Any(l => l is not ("zh-CN" or "zh-TW" or "ko" or "en" or "id" or "vi")) || languages.Distinct(StringComparer.Ordinal).Count() != languages.Count)) throw new InvalidDataException("Invalid client languages.");
    }

    private static void ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64 || password.Length == 0) throw new ArgumentException("Invalid account or password length.");
    }

    private static void ValidateProfile(AccountProfile profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.Username) || profile.Username.Length > 64 || profile.DisplayName is null || profile.DisplayName.Length > 128 || profile.Stats is null || profile.Stats.Keys.Any(k => k.Length > 64))
            throw new InvalidDataException("Invalid account profile.");
        foreach (var value in new[] { profile.RankVariant, profile.RankBadge })
            if (value is not null && (value.Length is < 1 or > 64 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                throw new InvalidDataException("Invalid rank presentation key.");
        if (profile.LevelIconId is < 0)
            throw new InvalidDataException("Rank icon identifiers must be non-negative.");
        if (profile.LevelIconUrl is not null && (profile.LevelIconUrl.Length > 2048 || !Uri.TryCreate(profile.LevelIconUrl, UriKind.Absolute, out var icon) || icon.Scheme != "https" || icon.UserInfo.Length != 0))
            throw new InvalidDataException("Rank icons must use HTTPS without credentials.");
    }

    /// <summary>Erases process-local session references and releases the owned HTTP transport.</summary>
    public void Dispose() { lock (_gate) { _sessions.Clear(); _catalogs.Clear(); } if (_ownsHttp) _http.Dispose(); }
}
