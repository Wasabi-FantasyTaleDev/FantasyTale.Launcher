using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Credentials explicitly retained for filling one server's login form.</summary>
public sealed class RememberedLogin(string username, string password)
{
    /// <summary>Account identifier used for the last successfully remembered login.</summary>
    public string Username { get; } = username;
    /// <summary>Password retrieved by the host for this saved server login.</summary>
    public string Password { get; } = password;
}

/// <summary>Stores explicitly opted-in passwords separately from refresh sessions.</summary>
public interface IRememberedLoginStore
{
    /// <summary>Reads the last remembered login belonging to this exact server endpoint.</summary>
    RememberedLogin? Read(string serverId, string apiUrl);
    /// <summary>Retains a successful login when opted in, or forgets that account's password when opted out.</summary>
    void Save(string serverId, string apiUrl, string username, string password, bool remember);
    /// <summary>Forgets passwords when their server is removed or edited.</summary>
    void Remove(string serverId);
}

/// <summary>Atomically stores opted-in login entries encrypted for the current Windows user.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedRememberedLoginStore : IRememberedLoginStore
{
    private sealed record Entry(string ServerId, string ApiUrl, string Username, string Password);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FantasyTale.Launcher.remembered-logins.v1");
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 8 };
    private readonly string _file;
    private readonly object _gate = new();

    /// <summary>Creates an independent protected file below the launcher's data directory.</summary>
    public ProtectedRememberedLoginStore(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "remembered-logins.bin");
        InstanceStore.RejectLinks(_file);
        Directory.CreateDirectory(dataRoot);
    }

    /// <inheritdoc />
    public RememberedLogin? Read(string serverId, string apiUrl)
    {
        lock (_gate)
        {
            var entry = Load().LastOrDefault(item => item.ServerId == serverId && item.ApiUrl == apiUrl);
            return entry is null ? null : new(entry.Username, entry.Password);
        }
    }

    /// <inheritdoc />
    public void Save(string serverId, string apiUrl, string username, string password, bool remember)
    {
        if (!Valid(new(serverId, apiUrl, username, password))) throw new ArgumentException("Invalid remembered login scope or credentials.");
        lock (_gate)
        {
            var entries = Load().Where(item => item.ServerId != serverId || item.ApiUrl == apiUrl && item.Username != username).ToList();
            if (remember) entries.Add(new(serverId, apiUrl, username, password));
            Write(entries);
        }
    }

    /// <inheritdoc />
    public void Remove(string serverId)
    {
        lock (_gate) Write(Load().Where(item => item.ServerId != serverId).ToArray());
    }

    private Entry[] Load()
    {
        InstanceStore.RejectLinks(_file);
        try
        {
            if (!File.Exists(_file)) return [];
            var encrypted = File.ReadAllBytes(_file);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var entries = JsonSerializer.Deserialize<Entry[]>(plain, Json);
                if (entries is null || entries.Any(entry => entry is null || !Valid(entry)) ||
                    entries.GroupBy(entry => (entry.ServerId, entry.ApiUrl, entry.Username)).Any(group => group.Count() > 1)) return [];
                return entries;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (CryptographicException) { return []; }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }

    private static bool Valid(Entry entry) => entry.ServerId is { Length: > 0 and <= 64 } &&
        entry.ServerId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') &&
        entry.ApiUrl is { Length: > 0 and <= 2048 } && Uri.TryCreate(entry.ApiUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 &&
        entry.Username is { Length: > 0 and <= 64 } && !string.IsNullOrWhiteSpace(entry.Username) && !entry.Username.Any(char.IsControl) &&
        entry.Password is { Length: > 0 } && !entry.Password.Any(char.IsControl);

    private void Write(IReadOnlyList<Entry> entries)
    {
        InstanceStore.RejectLinks(_file);
        if (entries.Count == 0) { File.Delete(_file); return; }
        var plain = JsonSerializer.SerializeToUtf8Bytes(entries, Json);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var temp = _file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(encrypted); output.Flush(true); }
            File.Move(temp, _file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
