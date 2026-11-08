using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Durable reviewed servers and bounded, process-local room leases.</summary>
public sealed class DirectoryRegistry : IDisposable
{
    private sealed record Snapshot(int Schema, DirectoryEntry[] Servers, DirectorySubmission[] Pending);
    private sealed record Room(DirectoryEntry Entry, byte[] TokenHash);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 12 };
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly string _file;
    private readonly FileStream _lease;
    private readonly int _maximum, _ttl;
    private Snapshot _state = new(1, [], []);
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Exclusively opens a directory store; corrupt or oversized state fails without resetting it.</summary>
    public DirectoryRegistry(string root, int maximum = 512, int roomLeaseSeconds = 90, TimeProvider? clock = null)
    {
        if (maximum is < 1 or > 1024 || roomLeaseSeconds is < 30 or > 300) throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum; _ttl = roomLeaseSeconds; _clock = clock ?? TimeProvider.System;
        root = Path.GetFullPath(root); InstanceStore.RejectLinks(root); Directory.CreateDirectory(root);
        _file = Path.Combine(root, "directory.json"); InstanceStore.RejectLinks(_file);
        var lockPath = Path.Combine(root, ".lock"); InstanceStore.RejectLinks(lockPath);
        _lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (!File.Exists(_file)) return;
            if (new FileInfo(_file).Length > 8 * 1024 * 1024) throw new InvalidDataException("Directory state exceeds its byte limit.");
            var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllBytes(_file), Json) ?? throw new InvalidDataException("Missing directory state.");
            if (saved.Schema != 1 || saved.Servers is null || saved.Pending is null || saved.Servers.Length > maximum || saved.Pending.Length > maximum)
                throw new InvalidDataException("Invalid directory state or capacity.");
            foreach (var entry in saved.Servers)
            {
                DirectoryValidation.Id(entry.Id); _ = DirectoryValidation.Normalize(entry.Details);
                if (entry.Kind != "server" || entry.ExpiresAt is not null) throw new InvalidDataException("Persistent state contains an invalid server.");
            }
            foreach (var submission in saved.Pending) { DirectoryValidation.Id(submission.Id); _ = DirectoryValidation.Normalize(submission.Details); }
            if (saved.Servers.Select(e => e.Id).Distinct().Count() != saved.Servers.Length || saved.Pending.Select(e => e.Id).Distinct().Count() != saved.Pending.Length)
                throw new InvalidDataException("Duplicate directory identities.");
            _state = saved;
        }
        catch { _lease.Dispose(); throw; }
    }

    /// <summary>Returns approved servers and unexpired rooms; pending submissions never appear here.</summary>
    public DirectoryListing List()
    {
        lock (_gate) { Check(); Expire(); return Copy(new DirectoryListing(1, _state.Servers.Concat(_rooms.Values.Select(r => r.Entry)).ToArray())); }
    }

    /// <summary>Returns the bounded review queue to an authenticated source administrator.</summary>
    public IReadOnlyList<DirectorySubmission> Pending()
    { lock (_gate) { Check(); return Copy(_state.Pending); } }

    /// <summary>Queues a persistent server for review without changing any approved listing.</summary>
    public DirectorySubmission Submit(DirectoryAdvertisement details)
    {
        details = DirectoryValidation.Normalize(details);
        lock (_gate)
        {
            Check();
            if (_state.Pending.Any(s => SameDestination(s.Details, details))) throw new InvalidOperationException("This destination already awaits review.");
            if (_state.Pending.Length >= _maximum) throw new InvalidOperationException("Directory submission capacity reached.");
            var submission = new DirectorySubmission("submission-" + Guid.NewGuid().ToString("N"), details, _clock.GetUtcNow());
            Save(_state with { Pending = [.. _state.Pending, submission] }); return Copy(submission);
        }
    }

    /// <summary>Approves or rejects an exact pending revision; updates retain the approved destination's identity.</summary>
    public void Review(string id, bool approve)
    {
        lock (_gate)
        {
            Check(); var pending = _state.Pending.SingleOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException("Submission no longer exists.");
            var next = _state with { Pending = _state.Pending.Where(s => s.Id != id).ToArray() };
            if (approve)
            {
                var existing = next.Servers.SingleOrDefault(e => SameDestination(e.Details, pending.Details));
                if (existing is null && next.Servers.Length >= _maximum) throw new InvalidOperationException("Directory server capacity reached.");
                var entry = new DirectoryEntry(existing?.Id ?? "server-" + Guid.NewGuid().ToString("N"), "server", pending.Details, null);
                next = next with { Servers = [.. next.Servers.Where(e => e.Id != entry.Id), entry] };
            }
            Save(next);
        }
    }

    /// <summary>Removes an approved persistent server through the administrator surface.</summary>
    public void RemoveServer(string id)
    {
        lock (_gate)
        {
            Check(); if (!_state.Servers.Any(e => e.Id == id)) throw new KeyNotFoundException("Server no longer exists.");
            Save(_state with { Servers = _state.Servers.Where(e => e.Id != id).ToArray() });
        }
    }

    /// <summary>Creates a new temporary room and one private renew/delete token.</summary>
    public DirectoryLease Publish(DirectoryAdvertisement details)
    {
        details = DirectoryValidation.Normalize(details);
        lock (_gate)
        {
            Check(); Expire();
            if (_rooms.Count >= _maximum) throw new InvalidOperationException("Directory room capacity reached.");
            var id = "room-" + Guid.NewGuid().ToString("N"); var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var expires = _clock.GetUtcNow().AddSeconds(_ttl);
            _rooms.Add(id, new(new(id, "room", details, expires), Hash(token)));
            return new(id, token, expires, _ttl / 3);
        }
    }

    /// <summary>Renews only an unexpired room using its exact private token; expired identities cannot be resurrected.</summary>
    public DirectoryLease Renew(string id, string token)
    {
        lock (_gate)
        {
            Check(); Expire(); var room = Authorized(id, token); var expires = _clock.GetUtcNow().AddSeconds(_ttl);
            _rooms[id] = room with { Entry = room.Entry with { ExpiresAt = expires } };
            return new(id, token, expires, _ttl / 3);
        }
    }

    /// <summary>Withdraws an exact room; knowledge of its public identity does not grant management access.</summary>
    public void Withdraw(string id, string token)
    { lock (_gate) { Check(); Expire(); _ = Authorized(id, token); _rooms.Remove(id); } }

    private Room Authorized(string id, string token)
    {
        if (token is null || token.Length != 64 || !_rooms.TryGetValue(id, out var room) || !CryptographicOperations.FixedTimeEquals(room.TokenHash, Hash(token)))
            throw new UnauthorizedAccessException("Room lease is missing, expired or belongs to another publisher.");
        return room;
    }
    private static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void Expire() { foreach (var id in _rooms.Where(p => p.Value.Entry.ExpiresAt <= _clock.GetUtcNow()).Select(p => p.Key).ToArray()) _rooms.Remove(id); }
    private static bool SameDestination(DirectoryAdvertisement a, DirectoryAdvertisement b) => a.Game == b.Game && a.ApiUrl == b.ApiUrl;
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
    private void Save(Snapshot next)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
        if (bytes.Length > 8 * 1024 * 1024) throw new IOException("Directory state exceeds its byte limit.");
        var temporary = _file + ".saving"; InstanceStore.RejectLinks(temporary); InstanceStore.RejectLinks(_file);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, _file, true); _state = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Invalidates volatile leases and releases the single-writer lock after request draining.</summary>
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _rooms.Clear(); _lease.Dispose(); } }
}
