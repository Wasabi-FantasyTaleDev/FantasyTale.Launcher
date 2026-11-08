using System.Text;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Atomic TOML storage for saved server destinations, separate from discovery and credentials.</summary>
public sealed class ServerStore
{
    private readonly string _root;

    /// <summary>Opens the local server definition directory, rejecting reparse paths.</summary>
    public ServerStore(string root)
    {
        _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root);
    }

    /// <summary>Combines independent bundled profiles with local overrides and user-added profiles.</summary>
    public IReadOnlyList<ServerDefinition> List(IReadOnlyList<GameDefinition> games)
    {
        var result = Bundled(games).ToDictionary(server => server.Id, StringComparer.OrdinalIgnoreCase);
        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(_root, "*.toml");
        foreach (var file in files)
        {
            var server = ReadProfile(file, false);
            var id = server.Id;
            if (!localIds.Add(id)) throw new DefinitionException(file, "server.id", "duplicate local server identity");
            if (!games.Any(g => g.Id == server.Game) || result.TryGetValue(id, out var bundled) && bundled.Game != server.Game)
                throw new DefinitionException(file, "server.game", "unknown or mismatched game");
            if (result.TryGetValue(id, out var bundledServer))
            {
                if (server.ApiUrl is { } address && bundledServer.ApiUrl is not null &&
                    Uri.TryCreate(address, UriKind.Absolute, out var previous) && previous.Host.Equals("bf.wanwanlocksmith.pp.ua", StringComparison.OrdinalIgnoreCase))
                    server = server with { ApiUrl = bundledServer.ApiUrl, PreferIp = true };
                server = server with { BuiltIn = true, IconAsset = bundledServer.IconAsset,
                    Presentation = MergePresentation(bundledServer.Presentation, server.Presentation) };
            }
            result[id] = server;
        }
        return result.Values.OrderByDescending(s => s.BuiltIn).ThenBy(s => s.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Loads each game's independent bundled server profiles, using identities inside the files rather than file names.</summary>
    public static IReadOnlyList<ServerDefinition> Bundled(IReadOnlyList<GameDefinition> games)
    {
        var result = new Dictionary<string, ServerDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            var folder = Path.Combine(game.Folder, "servers");
            if (!Directory.Exists(folder)) continue;
            InstanceStore.RejectLinks(folder);
            foreach (var file in Directory.EnumerateFiles(folder, "*.toml").Order(StringComparer.Ordinal))
            {
                var server = ReadProfile(file, true);
                if (server.Game != game.Id) throw new DefinitionException(file, "server.game", "must match the containing game");
                if (!result.TryAdd(server.Id, server)) throw new DefinitionException(file, "server.id", "duplicate bundled server identity");
            }
        }
        return result.Values.ToArray();
    }

    private static ServerDefinition ReadProfile(string file, bool builtIn)
    {
        InstanceStore.RejectLinks(file);
        return ServerProfileFile.Load(file, builtIn);
    }

    private static ServerPresentation? MergePresentation(ServerPresentation? defaults, ServerPresentation? local)
    {
        if (defaults is null) return local;
        if (local is null) return defaults;
        static IReadOnlyDictionary<string, T> Merge<T>(IReadOnlyDictionary<string, T> first, IReadOnlyDictionary<string, T> second)
        {
            var result = new Dictionary<string, T>(first, StringComparer.OrdinalIgnoreCase);
            foreach (var (language, value) in second) result[language] = value;
            return result;
        }
        return local with { MotdTranslations = Merge(defaults.MotdTranslations, local.MotdTranslations),
            SummaryTranslations = Merge(defaults.SummaryTranslations, local.SummaryTranslations),
            IconUrlTranslations = Merge(defaults.IconUrlTranslations, local.IconUrlTranslations),
            ImagesTranslations = Merge(defaults.ImagesTranslations, local.ImagesTranslations) };
    }

    /// <summary>Saves one destination under a cross-process lease, optionally rejecting an existing identity for a new-server form.</summary>
    public void Save(ServerDefinition server, bool createOnly = false)
    {
        Validate(server);
        var lockFile = Path.Combine(_root, ".lock"); InstanceStore.RejectLinks(lockFile);
        using var lease = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (createOnly && ExistingProfile(server.Id) is not null) throw new InvalidOperationException("server_id_exists");
        Write(server);
    }

    /// <summary>Saves a refreshed profile only when the persisted snapshot still matches the caller's view.</summary>
    public void SaveIfUnchanged(ServerDefinition expected, ServerDefinition replacement, IReadOnlyList<GameDefinition> games)
    {
        Validate(replacement);
        var lockFile = Path.Combine(_root, ".lock"); InstanceStore.RejectLinks(lockFile);
        using var lease = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = List(games).SingleOrDefault(server => server.Id.Equals(expected.Id, StringComparison.OrdinalIgnoreCase));
        if (current is null || !ServerProfileFile.Format(current).Equals(ServerProfileFile.Format(expected), StringComparison.Ordinal))
            throw new InvalidOperationException("server_profile_conflict");
        Write(replacement with { Id = current.Id, BuiltIn = current.BuiltIn, IconAsset = current.IconAsset });
    }

    private void Write(ServerDefinition server)
    {
        var file = ExistingProfile(server.Id) ?? NewProfile(server);
        InstanceStore.RejectLinks(file);
        var text = ServerProfileFile.Format(server);
        var temporary = Path.Combine(_root, ".saving-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Checks a profile against saved identities and connection endpoints without writing any data.</summary>
    public ServerProfileImport InspectImport(ServerDefinition imported, IReadOnlyList<GameDefinition> games)
    {
        Validate(imported);
        if (!games.Any(game => game.Id == imported.Game)) throw new ArgumentException("Unknown profile game.");
        var saved = List(games);
        var existing = saved.SingleOrDefault(server => server.Id.Equals(imported.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Game != imported.Game) throw new ArgumentException("A server identity cannot change games.");
        existing ??= saved.FirstOrDefault(server => server.Game == imported.Game && SameDestination(server, imported));
        if (existing is null && imported.Id.StartsWith("local-", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Reserved server identity.");
        var server = imported with { Id = existing?.Id ?? imported.Id, BuiltIn = existing?.BuiltIn ?? false, IconAsset = existing?.IconAsset };
        var text = ServerProfileFile.Format(server);
        var previous = existing is null ? null : ServerProfileFile.Format(existing);
        return new(server, existing is not null, text == previous, previous is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(previous))));
    }

    /// <summary>Imports atomically, requiring the exact inspected fingerprint before replacing different saved settings.</summary>
    public ServerDefinition Import(ServerDefinition imported, IReadOnlyList<GameDefinition> games, string? expectedFingerprint = null)
    {
        var lockFile = Path.Combine(_root, ".lock"); InstanceStore.RejectLinks(lockFile);
        using var lease = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var inspection = InspectImport(imported, games);
        if (inspection.Identical) return inspection.Server;
        if (inspection.Exists && inspection.Fingerprint != expectedFingerprint || !inspection.Exists && expectedFingerprint is not null)
            throw new InvalidOperationException("server_profile_conflict");
        Write(inspection.Server);
        return inspection.Server;
    }

    private static bool SameDestination(ServerDefinition first, ServerDefinition second) =>
        first.Endpoint is { } a && second.Endpoint is { } b && a.Port == b.Port && a.Host.TrimEnd('.').Equals(b.Host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
        || first.ApiUrl is not null && second.ApiUrl is not null && ApiBase(first.ApiUrl) == ApiBase(second.ApiUrl);

    /// <summary>Removes a user-added server; bundled destinations remain available.</summary>
    public void Delete(ServerDefinition server)
    {
        Validate(server);
        if (server.BuiltIn) throw new InvalidOperationException("Bundled servers cannot be removed.");
        var lockFile = Path.Combine(_root, ".lock"); InstanceStore.RejectLinks(lockFile);
        using var lease = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (ExistingProfile(server.Id) is { } file) File.Delete(file);
    }

    private string? ExistingProfile(string id)
    {
        var matches = Directory.EnumerateFiles(_root, "*.toml").Where(file => ReadProfile(file, false).Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch { 0 => null, 1 => matches[0], _ => throw new InvalidDataException("Duplicate local server profile identity.") };
    }

    private string NewProfile(ServerDefinition server)
    {
        var file = Path.Combine(_root, ServerProfileFile.FileName(server));
        if (File.Exists(file)) throw new InvalidOperationException("server_profile_conflict");
        return file;
    }

    /// <summary>Accepts file-safe ASCII letters, digits, hyphens and underscores, excluding Windows device names.</summary>
    public static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new ArgumentException("server_id_invalid");
        var name = id.ToLowerInvariant();
        if (name is "con" or "prn" or "aux" or "nul" || name.Length == 4 && (name.StartsWith("com", StringComparison.Ordinal) || name.StartsWith("lpt", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9')
            throw new ArgumentException("server_id_reserved");
    }

    /// <summary>Generates a stable initial identity from a manual endpoint or API authority; later address changes retain it.</summary>
    public static string DefaultId(string? apiUrl, ServerEndpoint? endpoint)
    {
        var api = string.IsNullOrWhiteSpace(apiUrl) ? null : ApiBase(apiUrl);
        var host = endpoint?.Host ?? api?.DnsSafeHost ?? throw new ArgumentException("server_address_required");
        var port = endpoint?.Port ?? api!.Port;
        var id = string.Concat(host.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_'))
            + "_" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ValidateId(id); return id;
    }

    /// <summary>Validates printable metadata and transport addresses before storage or connection.</summary>
    public static void Validate(ServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);
        ValidateId(server.Id); InstanceFile.ValidateId("server.toml", server.Game);
        if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 128 || server.Name.Any(char.IsControl)) throw new ArgumentException("Server name must contain 1..128 printable characters.");
        if (server.ApiUrl is not null) _ = ApiBase(server.ApiUrl);
        if (server.IconAsset is { } asset && (asset.Any(char.IsControl) || asset.Contains('\\') || asset.IndexOfAny([':', '?', '#']) >= 0 || asset.Split('/').Any(segment => segment is "" or "." or "..")))
            throw new ArgumentException("Bundled icon must be a relative web asset path.");
        if (server.Endpoint is { } e && (e.Port is < 1 or > 65535 || e.Host.Length > 253 || Uri.CheckHostName(e.Host) == UriHostNameType.Unknown)) throw new ArgumentException("Invalid game endpoint.");
        if (server.Builds is null || server.Builds.Distinct(StringComparer.Ordinal).Count() != server.Builds.Count) throw new ArgumentException("Invalid supported builds.");
        foreach (var build in server.Builds) InstanceFile.ValidateId("server.toml", build);
        if (server.NativeDirectory is { } directory)
        {
            if (directory.Mode is not ("native" or "gateway") || directory.Builds is null || directory.Builds.Count == 0
                || directory.Builds.Distinct(StringComparer.Ordinal).Count() != directory.Builds.Count
                || directory.Targets is null || directory.Targets.Count is < 1 or > 32)
                throw new ArgumentException("Invalid native server directory.");
            foreach (var build in directory.Builds) InstanceFile.ValidateId("server.toml", build);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in directory.Targets)
            {
                if (target is null || string.IsNullOrWhiteSpace(target.Id) || target.Id.Length > 128
                    || !target.Id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
                    || string.IsNullOrWhiteSpace(target.Name) || target.Name.Length > 128 || target.Name.Any(char.IsControl)
                    || target.ShortName is { Length: 0 or > 128 } || target.ShortName?.Any(char.IsControl) == true
                    || target.Endpoint is null || target.Endpoint.Port is < 1 or > 65535
                    || target.Endpoint.Host.Length > 253 || Uri.CheckHostName(target.Endpoint.Host) == UriHostNameType.Unknown
                    || !ids.Add(target.Id)
                    || !endpoints.Add(target.Endpoint.Host.TrimEnd('.') + ":" + target.Endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    throw new ArgumentException("Invalid or duplicate native server directory target.");
            }
        }
        ServerMetadataPolicy.Validate(server.ClientPolicy);
        var presentation = server.Presentation;
        ServerMetadataPolicy.Validate(presentation is null ? null : presentation with
        {
            IconUrl = presentation.IconUrl is { } icon && ServerArtworkStore.IsReference(icon) ? null : presentation.IconUrl,
            Images = presentation.Images?.Where(image => !ServerArtworkStore.IsReference(image)).ToArray()!,
            IconUrlTranslations = presentation.IconUrlTranslations?.ToDictionary(pair => pair.Key, pair => ServerArtworkStore.IsReference(pair.Value) ? "" : pair.Value)!,
            ImagesTranslations = presentation.ImagesTranslations?.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value?.Where(image => !ServerArtworkStore.IsReference(image)).ToArray()!)!,
        });
    }

    /// <summary>Allows HTTP(S) APIs; credentials cannot enter a URL.</summary>
    public static Uri ApiBase(string value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Scheme is not ("https" or "http")) throw new ArgumentException("API URL must use HTTP or HTTPS, without credentials, query or fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}

/// <summary>A read-only import comparison; its fingerprint authorizes replacement of only the inspected saved state.</summary>
/// <param name="Server">Validated imported settings with any existing local identity retained.</param>
/// <param name="Exists">Whether the identity or endpoint is already saved.</param>
/// <param name="Identical">Whether all persisted settings already match.</param>
/// <param name="Fingerprint">Digest of the existing settings, required only after explicit overwrite confirmation.</param>
public sealed record ServerProfileImport(ServerDefinition Server, bool Exists, bool Identical, string? Fingerprint);
