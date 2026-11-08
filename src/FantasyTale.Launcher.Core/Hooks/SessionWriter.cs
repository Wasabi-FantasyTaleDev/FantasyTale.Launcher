using System.Globalization;
using System.Text;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>One plugin entry of a session.</summary>
/// <param name="Id">Plugin id.</param>
/// <param name="Version">Plugin version the launcher resolved.</param>
/// <param name="Folder">Absolute folder holding the plugin's <c>plugin.toml</c>.</param>
/// <param name="Required">Whether a failure must be reported to the user.</param>
/// <param name="Options">Option values: <see cref="bool"/>, <see cref="long"/> or <see cref="string"/>.</param>
public sealed record SessionPluginSpec(string Id, string Version, string Folder, bool Required, IReadOnlyDictionary<string, object> Options);

/// <summary>The <c>[messenger]</c> section: how the client's messenger reaches the server's messenger service.</summary>
/// <param name="Transport">"http" when the launcher's stand-in calls the service directly; the only transport so far.</param>
/// <param name="Url">Absolute http(s) base URL of the messenger service.</param>
/// <param name="Ticket">Signed messenger ticket; a secret like the login ticket.</param>
public sealed record SessionMessenger(string Transport, string Url, string Ticket)
{
    /// <summary>True when <paramref name="url"/> is an http(s) URL the native runtime accepts: no credentials, query, fragment or whitespace.</summary>
    public static bool IsValidUrl(string url) =>
        url.Length is > 0 and <= 2048 && !url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '?' or '#' or '\\')
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && uri.Host.Length > 0 && uri.UserInfo.Length == 0 && !url.Contains('@', StringComparison.Ordinal);

    /// <summary>True when <paramref name="ticket"/> has the native runtime's ticket alphabet and length.</summary>
    public static bool IsValidTicket(string ticket) =>
        ticket.Length is > 0 and <= 2048 && ticket.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

/// <summary>
/// The <c>[game_login]</c> section: a launch whose player signs in on the game's own login screen. The stand-in sends
/// the launcher's sign-in (<c>v1/auth/login</c>) with the typed account, then <c>v1/launch</c> with this launch.
/// </summary>
/// <param name="ApiUrl">The launcher API base URL the sign-in uses (the server's secure URL when it publishes one).</param>
/// <param name="LaunchId">The in-game sign-in launch the server opened.</param>
/// <param name="LaunchKey">The launch's secret.</param>
/// <param name="LauncherVersion">The launcher version the requests report, as the launcher's own do.</param>
/// <param name="Build">The wire build the requests name, such as id-400.</param>
/// <param name="ProtocolVersion">The build's protocol version.</param>
/// <param name="ReleaseVersion">The installed release label, or null.</param>
/// <param name="PatchVersion">The installed patch label, or null.</param>
/// <param name="LauncherLanguage">The launcher UI language, or empty.</param>
/// <param name="NativeLanguage">The build's native language, or empty.</param>
/// <param name="ClientLanguage">The selected client language, or empty.</param>
public sealed record SessionGameLogin(string ApiUrl, string LaunchId, string LaunchKey, string LauncherVersion, string Build, int ProtocolVersion,
    string? ReleaseVersion, string? PatchVersion, string LauncherLanguage, string NativeLanguage, string ClientLanguage);

/// <summary>Everything written into <c>session.toml</c> (docs/hook/session-contract.md).</summary>
/// <param name="Game">Game id.</param>
/// <param name="Build">The instance's build.</param>
/// <param name="Instance">Instance id.</param>
/// <param name="LogPath">Absolute log path for the hook runtime.</param>
/// <param name="Account">Launcher account, or null.</param>
/// <param name="Ticket">Server login ticket, or null.</param>
/// <param name="Endpoint">Server endpoint for plugins that redirect it, or null.</param>
/// <param name="Plugins">Plugins to load, in order.</param>
public sealed record SessionSpec(
    string Game,
    BuildDefinition Build,
    string Instance,
    string LogPath,
    string? Account,
    string? Ticket,
    ServerEndpoint? Endpoint,
    IReadOnlyList<SessionPluginSpec> Plugins)
{
    /// <summary>Selected game language, independent of the launcher's interface language; omitted for older runtimes.</summary>
    public string? ClientLanguage { get; init; }
    /// <summary>Random per-start identifier enabling the schema 2 native readiness contract; null writes a legacy session.</summary>
    public string? LaunchId { get; init; }

    /// <summary>Maximum time allowed for native initialization before authentication is refused.</summary>
    public int StartupTimeoutMs { get; init; } = 120000;

    /// <summary>Optional per-launch broker pipe for the overlay ABI; never contains credentials.</summary>
    public string? OverlayPipe { get; init; }

    /// <summary>Explicit plain-name room login; incompatible with a launch ticket.</summary>
    public bool NameLogin { get; init; }

    /// <summary>Selected native plugin receiving the module's explicit credential grant; omitted by default.</summary>
    public string? AuthenticationPlugin { get; init; }

    /// <summary>Messenger sign-in for the client; requires a schema 2 session.</summary>
    public SessionMessenger? Messenger { get; init; }

    /// <summary>An in-game sign-in launch; requires a schema 2 session without a ticket.</summary>
    public SessionGameLogin? GameLogin { get; init; }

    /// <summary>Validated game-module capability and payload delivered only to this launch's native session.</summary>
    public LaunchBootstrapMetadata? ModuleData { get; init; }
}

/// <summary>
/// Writes the session file the native hook runtime reads at client start. The file holds the
/// login ticket, so callers delete it when the client exits (<see cref="Delete"/>).
/// </summary>
public static class SessionWriter
{
    /// <summary>File name the native host looks for beside itself.</summary>
    public const string FileName = "session.toml";

    /// <summary>Renders <paramref name="spec"/> as TOML.</summary>
    public static string Render(SessionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.NameLogin && (spec.Ticket is not null || string.IsNullOrEmpty(spec.Account) || spec.Account.Length > 32
            || spec.Account.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or '@'))))
            throw new ArgumentException("Invalid plain-name login.", nameof(spec));
        if (spec.AuthenticationPlugin is { } owner && (string.IsNullOrWhiteSpace(owner)
            || spec.LaunchId is null || (!spec.NameLogin && string.IsNullOrEmpty(spec.Ticket)) || spec.Endpoint is null
            || spec.Plugins.Count(plugin => plugin.Id == owner) != 1))
            throw new ArgumentException("An authentication grant requires a launch identity, a credential for its mode, an endpoint and exactly one selected owner.", nameof(spec));
        var text = new StringBuilder();
        if (spec.ClientLanguage is { } clientLanguage && (clientLanguage.Length is < 1 or > 32
            || clientLanguage.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Invalid game language.", nameof(spec));
        text.Append("# Written by FantasyTale.Launcher for one client start; deleted when the client exits.\n");
        if (spec.LaunchId is { } launchId && (launchId.Length != 32 || launchId.Any(c => !char.IsAsciiHexDigitLower(c))))
            throw new ArgumentException("A launch id must contain 32 lower-case hexadecimal characters.", nameof(spec));
        if (spec.StartupTimeoutMs is < 1000 or > 300000) throw new ArgumentOutOfRangeException(nameof(spec));
        if (spec.OverlayPipe is { } overlayPipe && (overlayPipe.Length is < 1 or > 256 || overlayPipe.Any(char.IsControl)))
            throw new ArgumentException("The overlay broker pipe name is invalid.", nameof(spec));
        if (spec.Messenger is { } check && (spec.LaunchId is null || check.Transport != "http"
            || !SessionMessenger.IsValidUrl(check.Url) || !SessionMessenger.IsValidTicket(check.Ticket)))
            throw new ArgumentException("A messenger section requires a launch identity, the http transport, an http(s) URL and a well-formed ticket.", nameof(spec));
        if (spec.GameLogin is { } game && (spec.LaunchId is null || spec.Ticket is not null || spec.NameLogin || !SessionMessenger.IsValidUrl(game.ApiUrl)
            || !IsToken(game.LaunchId) || !IsToken(game.LaunchKey) || game.Build.Length is 0 or > 64 || game.Build.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
            || game.ProtocolVersion is < 1 or > 65535 || game.LauncherVersion.Length is 0 or > 64 || game.LauncherVersion.Any(char.IsControl)))
            throw new ArgumentException("An in-game sign-in requires a launch identity, no ticket, an http(s) API URL, the server's launch and a build.", nameof(spec));
        if (spec.ModuleData is { } data && (spec.LaunchId is null || data.Capability is not { Length: > 0 and <= 128 }
            || !data.Capability.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')
            || string.IsNullOrEmpty(data.Payload) || Encoding.UTF8.GetByteCount(data.Payload) > 32768
            || !IsValidUnicode(data.Payload)))
            throw new ArgumentException("Module data requires a launch identity, a capability identifier and a valid payload within the native 32768-byte limit.", nameof(spec));
        text.Append(spec.LaunchId is null ? "schema = 1\n\n[session]\n" : "schema = 2\n\n[session]\n");
        if (spec.LaunchId is not null)
        {
            Line(text, "launch_id", spec.LaunchId);
            text.Append(CultureInfo.InvariantCulture, $"startup_timeout_ms = {spec.StartupTimeoutMs}\n");
            text.Append(CultureInfo.InvariantCulture, $"launcher_pid = {Environment.ProcessId}\n");
        }
        Line(text, "game", spec.Game);
        Line(text, "build", spec.Build.Id);
        Line(text, "branch", spec.Build.Branch);
        if (spec.ClientLanguage is { } selectedLanguage) Line(text, "client_language", selectedLanguage);
        Line(text, "instance", spec.Instance);
        if (spec.Build.Messenger.Length > 0) Line(text, "messenger", spec.Build.Messenger);
        Line(text, "log", spec.LogPath);
        if (spec.Build.PeTimestamp is { } timestamp)
            text.Append(CultureInfo.InvariantCulture, $"\n[session.fingerprint]\npe_timestamp = 0x{timestamp:X8}\n");
        if (spec.Account is not null || spec.Ticket is not null)
        {
            text.Append("\n[login]\n");
            if (spec.NameLogin) Line(text, "mode", "name");
            if (spec.Account is not null) Line(text, "account", spec.Account);
            if (spec.Ticket is not null) Line(text, "ticket", spec.Ticket);
            if (spec.AuthenticationPlugin is { } authenticationPlugin) Line(text, "credential_plugin", authenticationPlugin);
        }
        if (spec.Endpoint is { } endpoint)
        {
            text.Append("\n[endpoint]\n");
            Line(text, "host", endpoint.Host);
            text.Append(CultureInfo.InvariantCulture, $"port = {endpoint.Port}\n");
        }
        if (spec.OverlayPipe is { } pipe)
        {
            text.Append("\n[overlay]\n");
            Line(text, "pipe", pipe);
        }
        if (spec.Messenger is { } messenger)
        {
            text.Append("\n[messenger]\n");
            Line(text, "transport", messenger.Transport);
            Line(text, "url", messenger.Url);
            Line(text, "ticket", messenger.Ticket);
        }
        if (spec.GameLogin is { } login)
        {
            text.Append("\n[game_login]\n");
            Line(text, "api_url", login.ApiUrl);
            Line(text, "launch_id", login.LaunchId);
            Line(text, "launch_key", login.LaunchKey);
            Line(text, "launcher_version", login.LauncherVersion);
            Line(text, "build", login.Build);
            text.Append(CultureInfo.InvariantCulture, $"protocol_version = {login.ProtocolVersion}\n");
            if (login.ReleaseVersion is { Length: > 0 } release) Line(text, "release_version", release);
            if (login.PatchVersion is { Length: > 0 } patch) Line(text, "patch_version", patch);
            if (login.LauncherLanguage.Length > 0) Line(text, "launcher_language", login.LauncherLanguage);
            if (login.NativeLanguage.Length > 0) Line(text, "native_language", login.NativeLanguage);
            if (login.ClientLanguage.Length > 0) Line(text, "client_language", login.ClientLanguage);
        }
        if (spec.ModuleData is { } moduleData)
        {
            text.Append("\n[module_data]\n");
            Line(text, "capability", moduleData.Capability);
            Line(text, "payload", moduleData.Payload);
        }
        foreach (var plugin in spec.Plugins)
        {
            text.Append("\n[[plugins]]\n");
            Line(text, "id", plugin.Id);
            Line(text, "version", plugin.Version);
            Line(text, "path", plugin.Folder);
            if (plugin.Required) text.Append("required = true\n");
            if (plugin.Options.Count == 0) continue;
            text.Append("[plugins.options]\n");
            foreach (var (key, value) in plugin.Options.OrderBy(o => o.Key, StringComparer.Ordinal))
            {
                text.Append(Key(key)).Append(" = ");
                text.Append(value switch
                {
                    bool b => b ? "true" : "false",
                    long n => n.ToString(CultureInfo.InvariantCulture),
                    int n => n.ToString(CultureInfo.InvariantCulture),
                    string s => Quote(s),
                    _ => throw new ArgumentException($"Option '{key}' of '{plugin.Id}' has unsupported type {value.GetType().Name}."),
                });
                text.Append('\n');
            }
        }
        return text.ToString();
    }

    /// <summary>Writes the session into <paramref name="clientFolder"/> and returns its path.</summary>
    public static string Write(string clientFolder, SessionSpec spec)
    {
        var path = Path.Combine(clientFolder, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Render(spec), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>Atomically publishes a new bounded session, refusing to replace another launch's file.</summary>
    public static string Create(string clientFolder, SessionSpec spec)
    {
        var content = Render(spec);
        if (spec.Plugins.Count > 64 || Encoding.UTF8.GetByteCount(content) > 256 * 1024)
            throw new ArgumentException("The session exceeds native runtime limits.", nameof(spec));
        var path = Path.Combine(clientFolder, FileName);
        var temporary = Path.Combine(clientFolder, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(content);
            File.Move(temporary, path, overwrite: false);
            return path;
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>Removes the session file of <paramref name="clientFolder"/> if present.</summary>
    public static void Delete(string clientFolder) => File.Delete(Path.Combine(clientFolder, FileName));

    private static bool IsToken(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    private static bool IsValidUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\0' || char.IsLowSurrogate(value[index])) return false;
            if (char.IsHighSurrogate(value[index]) && (++index >= value.Length || !char.IsLowSurrogate(value[index]))) return false;
        }
        return true;
    }

    private static void Line(StringBuilder text, string key, string value) => text.Append(key).Append(" = ").Append(Quote(value)).Append('\n');

    private static string Key(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? key : Quote(key);

    /// <summary>A TOML basic string with every control character escaped.</summary>
    internal static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    else text.Append(c);
                    break;
            }
        }
        return text.Append('"').ToString();
    }
}
