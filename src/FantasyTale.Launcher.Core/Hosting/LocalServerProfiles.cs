using System.Net;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hosting;

/// <summary>Bounded atomic storage for local server settings, independent of long-running remote servers.</summary>
public sealed class LocalServerProfiles
{
    private readonly string _root;

    /// <summary>Opens the user's local hosting profile directory.</summary>
    public LocalServerProfiles(string root)
    { _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root); }

    /// <summary>Reads saved profiles and rejects inconsistent file identities.</summary>
    public IReadOnlyList<LocalServerProfile> List()
    {
        var files = Directory.EnumerateFiles(_root, "*.toml");
        return files.Select(file =>
        {
            InstanceStore.RejectLinks(file); var t = TomlReader.Load(file);
            t.OnlyKeys("schema", "id", "game", "build", "name", "package_path", "mode", "advertised_host", "game_port", "api_port", "admin_port", "temporary_data", "storage_mode", "require_authentication", "client_instance_id", "relay_port");
            if (t.Integer("schema", true) != 1) throw t.Error("schema", "must be 1");
            var profile = new LocalServerProfile(t.String("id", true), t.String("game", true), t.String("build", true), t.String("name", true),
                t.String("package_path", true), t.String("mode", true), t.String("advertised_host", true),
                (int)t.Integer("game_port", true, 1, 65535)!.Value, (int)t.Integer("api_port", true, 1, 65535)!.Value,
                (int)t.Integer("admin_port", true, 1, 65535)!.Value, t.Boolean("temporary_data")) { RelayPort = t.Has("relay_port") ? (int)t.Integer("relay_port", true, 1024, 65535)!.Value : null, StorageMode = t.Has("storage_mode") ? t.String("storage_mode", true) : null, RequireAuthentication = t.Boolean("require_authentication"),
                ClientInstanceId = t.Has("client_instance_id") ? t.String("client_instance_id", true) : null };
            Validate(profile);
            if (Path.GetFileNameWithoutExtension(file) != profile.Id) throw t.Error("id", "must match file name");
            return profile;
        }).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Validates display metadata, explicit network scope and separate listener ports.</summary>
    public static void Validate(LocalServerProfile profile)
    {
        foreach (var id in new[] { profile.Id, profile.Game, profile.Build }) InstanceFile.ValidateId("hosting.toml", id);
        if (profile.ClientInstanceId is { } clientId) InstanceFile.ValidateId("hosting.toml", clientId);
        if (profile.Id.Length > 58) throw new ArgumentException("Hosting id exceeds its limit.");
        if (profile.Name.Length is < 1 or > 128 || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Any(char.IsControl)) throw new ArgumentException("Invalid hosting name.");
        if (!Path.IsPathFullyQualified(profile.PackagePath) || profile.PackagePath.Length > 1024 || profile.PackagePath.Any(char.IsControl)) throw new ArgumentException("Select an absolute server package path.");
        InstanceStore.RejectLinks(profile.PackagePath);
        if (profile.Mode is not ("private" or "lan" or "room")) throw new ArgumentException("Hosting mode must be private, lan or room.");
        if (profile.Mode == "lan" && (!IPAddress.TryParse(profile.AdvertisedHost, out var lan) || !LocalServerNetwork.IsLan(lan)))
            throw new ArgumentException("LAN mode requires an explicit private IPv4 interface address.");
        if (profile.EffectiveStorageMode is not ("package" or "local" or "temporary")) throw new ArgumentException("Storage mode must be package, local or temporary.");
        if (profile.StorageMode is not null && profile.TemporaryData != (profile.StorageMode == "temporary"))
            throw new ArgumentException("Storage mode conflicts with the legacy temporary-data choice.");
        if (profile.AdvertisedHost.Length > 253 || Uri.CheckHostName(profile.AdvertisedHost) == UriHostNameType.Unknown ||
            profile.Mode == "private" && profile.AdvertisedHost != "127.0.0.1" ||
            profile.Mode == "room" && (profile.AdvertisedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(profile.AdvertisedHost, out var ip) && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))))
            throw new ArgumentException("Room mode needs a reachable host; private mode uses 127.0.0.1.");
        var ports = new[] { profile.GamePort, profile.EffectiveRelayPort, profile.ApiPort, profile.AdminPort };
        if (ports.Any(p => p is < 1024 or > 65535) || ports.Distinct().Count() != ports.Length) throw new ArgumentException("Choose four different ports between 1024 and 65535.");
    }

    /// <summary>Persists one profile under a cross-process lease without changing the installed server package.</summary>
    public void Save(LocalServerProfile profile)
    {
        Validate(profile);
        using var lease = Lease();
        var text = new StringBuilder("schema = 1\n");
        void Line(string key, string value) => text.Append(key).Append(" = ").Append(SessionWriter.Quote(value)).Append('\n');
        Line("id", profile.Id); Line("game", profile.Game); Line("build", profile.Build); Line("name", profile.Name);
        Line("package_path", Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.PackagePath)));
        Line("mode", profile.Mode); Line("advertised_host", profile.AdvertisedHost);
        if (profile.RelayPort is { } relayPort) text.Append("relay_port = ").Append(relayPort).Append('\n');
        if (profile.ClientInstanceId is { } clientId) Line("client_instance_id", clientId);
        text.Append("require_authentication = ").Append(profile.RequireAuthentication ? "true" : "false").Append('\n');
        if (profile.StorageMode is not null) Line("storage_mode", profile.StorageMode);
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"game_port = {profile.GamePort}\napi_port = {profile.ApiPort}\nadmin_port = {profile.AdminPort}\ntemporary_data = {(profile.TemporaryData ? "true" : "false")}\n");
        var path = Safe(profile.Id + ".toml"); var temp = Safe("saving.tmp");
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(Encoding.UTF8.GetBytes(text.ToString())); stream.Flush(true); }
        File.Move(temp, path, true);
    }

    /// <summary>Removes only the saved configuration; server files and player data are preserved.</summary>
    public void Delete(string id)
    { InstanceFile.ValidateId("hosting.toml", id); using var lease = Lease(); File.Delete(Safe(id + ".toml")); }

    private FileStream Lease() => new(Safe("profiles.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private string Safe(string file) { var path = Path.Combine(_root, file); InstanceStore.RejectLinks(path); return path; }
}
