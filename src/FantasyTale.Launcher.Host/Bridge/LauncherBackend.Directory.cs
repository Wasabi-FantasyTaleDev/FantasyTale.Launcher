using System.Text.Json;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private void RegisterDirectory(BridgeRouter bridge)
    {
        bridge.Add("directory.sources", _ => DirectorySources());
        bridge.Add("directory.publications", _ => _roomPublisher.List());
        bridge.Add("directory.onlinePlayers", async (p, ct) =>
        {
            Keys(p, "sourceId", "entryId");
            var source = DirectorySources().SingleOrDefault(s => s.Id == Text(p, "sourceId") && s.Enabled)
                ?? throw new ArgumentException("Unknown discovery source.");
            var listing = await ReadDirectoryListingAsync(source, ct).ConfigureAwait(false);
            var entry = listing.Entries.SingleOrDefault(e => e.Id == Text(p, "entryId") && (e.Kind != "room" || e.ExpiresAt > DateTimeOffset.UtcNow))
                ?? throw new InvalidOperationException("This server or room is no longer listed.");
            var details = DirectoryValidation.Normalize(entry.Details);
            if (details.Presentation?.RosterEnabled == false) return new OnlinePlayerList(1, false, []);
            return await _api.OnlinePlayersAsync(new("discovery", details.Game, details.Name, details.ApiUrl, details.Endpoint, false, false), ct).ConfigureAwait(false);
        });
        bridge.Add("directory.saveSource", async (p, ct) =>
        {
            Keys(p, "id", "name", "baseUrl", "enabled"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var id = Text(p, "id"); EnsureDirectoryIdle(id);
                var previous = DirectorySources().SingleOrDefault(s => s.Id == id);
                if (previous?.StoreSourceId is not null) throw new InvalidOperationException("Manage this source through the store source settings.");
                var source = new DirectorySource(id, Text(p, "name"), p?["baseUrl"]?.GetValue<string>(), p?["enabled"]?.GetValue<bool>() ?? true, previous?.BuiltIn ?? false);
                _directories.Save(source); _directoryConnections.DisconnectSource(id); return source;
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("directory.deleteSource", async (p, ct) =>
        {
            Keys(p, "id"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try { var id = Text(p, "id"); EnsureDirectoryIdle(id); if (DirectorySources().Any(source => source.Id == id && source.StoreSourceId is not null)) throw new InvalidOperationException("Manage this source through the store source settings."); _directories.Delete(id, _catalogs.DirectorySources(_sources.List())); _directoryConnections.DisconnectSource(id); return (object?)null; }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("directory.list", async (p, ct) =>
        {
            Keys(p, "sourceId"); if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many discovery requests.");
            try { return await ReadDirectoryListingAsync(DirectorySource(Text(p, "sourceId")), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("directory_timeout"); }
            finally { _catalogRequests.Release(); }
        });
        bridge.Add("directory.submit", async (p, ct) =>
        {
            Keys(p, "sourceId", "details");
            var details = p!["details"]!.Deserialize<DirectoryAdvertisement>(BridgeRouter.Json) ?? throw new ArgumentException("Server details are required.");
            _ = Game(details.Game);
            return await _directoryApi.SubmitAsync(MutableDirectorySource(Text(p, "sourceId")), details, ct).ConfigureAwait(false);
        });
        bridge.Add("directory.profile", async (p, ct) =>
        {
            Keys(p, "sourceId", "entryId");
            var source = DirectorySource(Text(p, "sourceId"));
            var listing = await ReadDirectoryListingAsync(source, ct).ConfigureAwait(false);
            var entry = listing.Entries.SingleOrDefault(entry => entry.Id == Text(p, "entryId") && entry.Kind == "server")
                ?? throw new InvalidOperationException("This server is no longer listed.");
            var profile = await ReadDiscoveredProfileAsync(source, entry, ct).ConfigureAwait(false);
            _ = Game(profile.Game);
            return new { Text = ServerProfileFile.Format(profile), Inspection = _servers.InspectImport(profile, Games()) };
        });
        bridge.Add("directory.join", async (p, ct) =>
        {
            Keys(p, "sourceId", "entryId", "build"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var source = DirectorySource(Text(p, "sourceId"));
                var listing = await ReadDirectoryListingAsync(source, ct).ConfigureAwait(false);
                var entry = listing.Entries.SingleOrDefault(e => e.Id == Text(p, "entryId")) ?? throw new InvalidOperationException("This room or server is no longer listed.");
                var build = Text(p, "build"); var game = Game(entry.Details.Game);
                if (entry.Kind == "room") game.RequireRoomsEnabled();
                if (!game.Builds.Any(b => b.Id == build) || !entry.Details.Builds.Contains(build, StringComparer.Ordinal)) throw new ArgumentException("Unsupported client build.");
                var server = DirectoryConnections.Destination(source, entry);
                var catalog = await _api.CatalogAsync(server, ct).ConfigureAwait(false);
                if (!catalog.Instances.Any(i => i.Build == build && i.Endpoint is not null)) throw new InvalidOperationException("The target server does not currently offer this client build.");
                if (entry.Kind == "room") _directoryConnections.Join(source, entry, build);
                else
                {
                    server = _servers.Import(await ReadDiscoveredProfileAsync(source, entry, ct).ConfigureAwait(false), Games());
                    _api.Forget(server.Id);
                }
                return server;
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("directory.publish", async (p, ct) =>
        {
            Keys(p, "id", "runId", "sourceId", "description"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var profile = _hostingProfiles.List().SingleOrDefault(s => s.Id == Text(p, "id")) ?? throw new ArgumentException("Unknown hosting profile.");
                Game(profile.Game).RequireRoomsEnabled();
                Game(profile.Game).RequireLocalHostingEnabled();
                return await _roomPublisher.PublishAsync(profile, Text(p, "runId"), MutableDirectorySource(Text(p, "sourceId")), p?["description"]?.GetValue<string>() ?? "", ct).ConfigureAwait(false);
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("directory.withdraw", async (p, ct) =>
        {
            Keys(p, "id", "runId"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try { await _roomPublisher.WithdrawAsync(Text(p, "id"), Text(p, "runId"), ct).ConfigureAwait(false); return (object?)null; }
            finally { _lifecycle.Release(); }
        });
    }

    private async Task<ServerDefinition> ReadDiscoveredProfileAsync(DirectorySource source, DirectoryEntry entry, CancellationToken ct)
    {
        if (source.StoreSourceId is not null)
        {
            var store = _sources.List().SingleOrDefault(store => store.Enabled && store.Id == entry.StoreSourceId
                && (source.StoreSourceId == "*" || source.StoreSourceId == store.Id)) ?? throw new ArgumentException("Unavailable store source.");
            return await StoreServerDiscovery.DownloadProfileAsync(_sourceHttp, OpenSource, store, entry, ct).ConfigureAwait(false);
        }
        var server = DirectoryConnections.Destination(source, entry);
        return server with { Presentation = entry.Details.Presentation, Builds = entry.Details.Builds,
            ClientPolicy = entry.Details.ClientPolicy };
    }

    private DirectorySource DirectorySource(string id) => DirectorySources().SingleOrDefault(s => s.Id == id && s.Enabled)
        ?? throw new ArgumentException("Unknown or disabled directory source.");
    private DirectorySource MutableDirectorySource(string id)
    {
        var source = DirectorySource(id);
        if (source.StoreSourceId is not null) throw new InvalidOperationException("This catalog is read-only. Select a live directory API to publish or submit entries.");
        return source;
    }
    private void EnsureDirectoryIdle(string id)
    { if (_roomPublisher.UsesSource(id)) throw new InvalidOperationException("Withdraw the published room before changing its directory source."); }
    private async Task<ServerDefinition> ConnectedServerAsync(string id, CancellationToken ct) =>
        await _directoryConnections.ResolveAsync(id, ct).ConfigureAwait(false) ?? Server(id);
}
