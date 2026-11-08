using FantasyTale.Launcher.Core.Servers;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private void RegisterServerProfiles(BridgeRouter bridge)
    {
        bridge.Add("servers.inspectProfile", p =>
        {
            Keys(p, "text", "game");
            var profile = ServerProfileFile.Parse("import.toml", Text(p, "text"));
            if (profile.Game != Text(p, "game")) throw new ArgumentException("Profile belongs to a different game.");
            return _servers.InspectImport(profile, Games());
        });
        bridge.Add("servers.importProfile", async (p, ct) =>
        {
            Keys(p, "text", "game", "expectedFingerprint");
            var profile = ServerProfileFile.Parse("import.toml", Text(p, "text"));
            if (profile.Game != Text(p, "game")) throw new ArgumentException("Profile belongs to a different game.");
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var server = _servers.Import(profile, Games(), p?["expectedFingerprint"]?.GetValue<string>());
                _api.Forget(server.Id);
                return server;
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("servers.exportProfile", p =>
        {
            Keys(p, "id");
            var server = SavedServers(Games()).SingleOrDefault(server => server.Id == Text(p, "id"))
                ?? throw new ArgumentException("Unknown saved server.");
            return new { FileName = ServerProfileFile.FileName(server), Text = ServerProfileFile.Format(server) };
        });
        bridge.Add("servers.refreshProfile", async (p, ct) =>
        {
            Keys(p, "id");
            var current = SavedServers(Games()).SingleOrDefault(server => server.Id == Text(p, "id"))
                ?? throw new ArgumentException("Unknown saved server.");
            var catalog = await _api.CatalogAsync(current, ct).ConfigureAwait(false);
            var refreshed = ServerProfileRefresh.Apply(current, catalog);
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try { _servers.SaveIfUnchanged(current, refreshed, Games()); _api.Forget(refreshed.Id); return refreshed; }
            finally { _lifecycle.Release(); }
        });
    }
}
