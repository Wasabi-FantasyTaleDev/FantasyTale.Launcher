using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>A refresh credential and the last validated public profile retained for one server account.</summary>
public sealed record PersistedAccountSession(string ServerId, string ApiUrl, string Username, string RefreshToken,
    DateTimeOffset RefreshExpiresAt, AccountProfile Profile, bool Selected);

/// <summary>Stores refresh credentials without exposing them to the frontend or client files.</summary>
public interface IAccountSessionStore
{
    /// <summary>Loads previously retained sessions, ignoring entries that cannot be decrypted or validated.</summary>
    IReadOnlyList<PersistedAccountSession> Load();

    /// <summary>Atomically replaces one server's retained account set.</summary>
    void Save(string serverId, string apiUrl, string? selected, IReadOnlyList<PersistedAccountSession> sessions);

    /// <summary>Removes all retained accounts for one server.</summary>
    void Remove(string serverId);
}

/// <summary>Windows-current-user protected storage for refresh credentials.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedAccountSessionStore : IAccountSessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FantasyTale.Launcher.account-sessions.v1");
    private readonly string _file;
    private readonly object _gate = new();

    /// <summary>Creates protected storage below the launcher data root.</summary>
    public ProtectedAccountSessionStore(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "account-sessions.bin");
        InstanceStore.RejectLinks(_file);
        Directory.CreateDirectory(dataRoot);
    }

    /// <inheritdoc />
    public IReadOnlyList<PersistedAccountSession> Load()
    {
        lock (_gate)
        {
            try
            {
                InstanceStore.RejectLinks(_file);
                if (!File.Exists(_file)) return [];
                var protectedBytes = File.ReadAllBytes(_file);
                var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    var values = JsonSerializer.Deserialize<List<PersistedAccountSession>>(plain, Json);
                    return values ?? [];
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch (CryptographicException) { return []; }
            catch (IOException) { return []; }
            catch (JsonException) { return []; }
        }
    }

    /// <inheritdoc />
    public void Save(string serverId, string apiUrl, string? selected, IReadOnlyList<PersistedAccountSession> sessions)
    {
        if (sessions.Any(session => session.ServerId != serverId || session.ApiUrl != apiUrl || session.Selected != (session.Username == selected)))
            throw new ArgumentException("Retained account scope or selection does not match its server.");
        lock (_gate)
        {
            var all = Load().Where(item => item is not null && item.ServerId != serverId && item.RefreshExpiresAt > DateTimeOffset.UtcNow).Concat(sessions).ToArray();
            Write(all);
        }
    }

    /// <inheritdoc />
    public void Remove(string serverId)
    {
        lock (_gate)
        {
            Write(Load().Where(item => item is not null && item.ServerId != serverId).ToArray());
        }
    }

    private void Write(PersistedAccountSession[] sessions)
    {
        InstanceStore.RejectLinks(_file);
        if (sessions.Length == 0) { File.Delete(_file); return; }
        var plain = JsonSerializer.SerializeToUtf8Bytes(sessions, Json);
        byte[] protectedBytes;
        try { protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var temporary = _file + ".tmp-" + Guid.NewGuid().ToString("N");
        InstanceStore.RejectLinks(temporary);
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(protectedBytes);
                file.Flush(true);
            }
            File.Move(temporary, _file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
