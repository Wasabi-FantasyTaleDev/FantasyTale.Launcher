using System.Net.Http;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    // Server sections, maintenance and notices of the selected server profile. A profile without a Launcher API, or a
    // server without a directory, has no sections and no server states, so nothing is shown and nothing blocks a launch.
    //
    // Sections share the profile's sign-in unless the directory gives them their own API. Such a separate section is
    // handled as its own server profile for everything bound to an account (sessions, remembered passwords, tickets),
    // under a derived identity, so its accounts never mix with the profile's own.
    private void RegisterServerStatus(BridgeRouter bridge)
    {
        bridge.Add("servers.status", async (p, ct) =>
        {
            Keys(p, "serverId", "build");
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false);
            if (server.ApiUrl is null) return ServerStatusView.Empty;
            var build = OptionalText(p, "build") ?? await DefaultBuildAsync(server, ct).ConfigureAwait(false);
            if (build is null) return ServerStatusView.Empty;
            var directory = await StatusDirectoryAsync(server, build, ct).ConfigureAwait(false);
            return directory is null ? ServerStatusView.Empty : ServerStatusView.From(directory);
        });
        bridge.Add("servers.notices", async (p, ct) =>
        {
            Keys(p, "serverId", "section", "all");
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false);
            if (server.ApiUrl is null) return Array.Empty<ServerNotice>();
            var section = OptionalText(p, "section");
            // A separate section publishes its own notices; the others read the profile's, filtered to the section.
            var notices = await _api.NoticesAsync(SectionServer(server, section), section, ct).ConfigureAwait(false);
            if (p?["all"]?.GetValue<bool>() == true) return notices;
            var unseen = _seenNotices.Unseen(SectionServer(server, section).Id, notices.Select(notice => notice.Id).ToArray());
            return notices.Where(notice => unseen.Contains(notice.Id)).ToArray();
        });
        bridge.Add("servers.markNoticesSeen", p =>
        {
            Keys(p, "serverId", "section", "ids");
            var ids = p?["ids"] is JsonArray array && array.Count <= 1024
                ? array.Select(id => id?.GetValue<string>() is { Length: > 0 and <= 64 } value ? value : throw new ArgumentException("Invalid notice identity.")).ToArray()
                : throw new ArgumentException("Invalid notice identities.");
            _seenNotices.MarkSeen(SectionServer(Server(Text(p, "serverId")), OptionalText(p, "section")).Id, ids);
            return null;
        });
    }

    private async Task<string?> DefaultBuildAsync(ServerDefinition server, CancellationToken ct) =>
        (await ReadCatalogAsync(server, ct, useCached: true).ConfigureAwait(false)).Instances is { Count: > 0 } presets ? presets[0].Build : null;

    // The profile's directory, kept on disk. When the profile's API cannot be reached the last directory read is used:
    // separate sections are asked through their own APIs, and servers that only the unreachable API vouches for are
    // shown offline, so working sections stay playable while the main node is down.
    private async Task<ServerDirectory?> StatusDirectoryAsync(ServerDefinition server, string build, CancellationToken ct)
    {
        try
        {
            var directory = await _api.ServerDirectoryAsync(server, build, configuredMembership: true, ct).ConfigureAwait(false);
            if (directory is not null) _sectionCache.Store(server.Id, directory);
            return directory;
        }
        catch (Exception error) when (Unreachable(error, ct) && _sectionCache.Directory(server.Id, build) is not null)
        {
            var cached = _sectionCache.Directory(server.Id, build)!;
            var live = new Dictionary<int, ServerSelector>();
            foreach (var section in (cached.Sections ?? []).Where(section => section.ApiUrl is not null))
            {
                try
                {
                    var own = await _api.ServerDirectoryAsync(SectionServer(server, section.Id), build, configuredMembership: true, ct).ConfigureAwait(false);
                    foreach (var selector in own?.Servers ?? []) if (selector.Section == section.Id) live[selector.Id] = selector;
                }
                catch (Exception sectionError) when (Unreachable(sectionError, ct)) { }
            }
            return cached with
            {
                Servers = cached.Servers.Select(selector => live.GetValueOrDefault(selector.Id)
                    ?? selector with { Targets = selector.Targets.Select(target => target with { Online = false }).ToArray() }).ToArray(),
            };
        }
    }

    private static bool Unreachable(Exception error, CancellationToken ct) => !ct.IsCancellationRequested && error is HttpRequestException
        or ServerFailureException { Code: "network_error" or "network_timeout" or "http_error" } or TaskCanceledException or IOException;

    /// <summary>The profile itself, or for a separate section a derived profile signing in through the section's API.</summary>
    private ServerDefinition SectionServer(ServerDefinition server, string? section)
    {
        if (section is null || server.ApiUrl is null) return server;
        var entry = _sectionCache.Sections(server.Id).FirstOrDefault(item => item.Id == section)
            ?? throw new ArgumentException("Unknown server section.");
        return entry.ApiUrl is null ? server : server with { Id = server.Id + "__" + section.Replace('.', '-'), ApiUrl = entry.ApiUrl };
    }

    private static string? OptionalText(JsonNode? p, string key) => p?[key]?.GetValue<string>() is { Length: > 0 } value ? value : null;

    /// <summary>The account sections of a profile with separate sections: the default one (the profile itself) and each separate one.</summary>
    private IReadOnlyList<(string Section, ServerDefinition Server)> AccountSections(ServerDefinition server)
    {
        var sections = _sectionCache.Sections(server.Id);
        if (server.ApiUrl is null || !sections.Any(section => section.ApiUrl is not null)) return [];
        return [(sections[0].Id, server), .. sections.Where(section => section.ApiUrl is not null).Select(section => (section.Id, SectionServer(server, section.Id)))];
    }

    // Every account of the profile and its separate sections, each marked with its section; the current account is the
    // one selected in the section last used. Without separate sections the list is the profile's own, as before.
    private async Task<AccountSessionList> SectionAccountsAsync(ServerDefinition server, bool refresh, CancellationToken ct)
    {
        var sections = AccountSections(server);
        if (sections.Count == 0)
        {
            if (refresh)
                try { await _api.RefreshIfNeededAsync(server, ct).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) when (_api.Current(server) is null) { }
            return Untagged(_api.Accounts(server));
        }
        var active = _sectionCache.ActiveSection(server.Id) ?? sections[0].Section;
        AccountProfile? current = null;
        var accounts = new List<AccountSessionInfo>();
        foreach (var (section, target) in sections)
        {
            if (refresh)
                try { await _api.RefreshIfNeededAsync(target, ct).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) { }
                catch (Exception error) when (Unreachable(error, ct)) { }
            var list = _api.Accounts(target);
            accounts.AddRange(list.Accounts.Select(info => info with { Account = info.Account with { Section = section } }));
            if (section == active && list.Current is { } selected) current = selected with { Section = section };
        }
        return new(current, accounts);
    }

    private static AccountSessionList Untagged(AccountSessionList list) => new(list.Current is null ? null : list.Current with { Section = null },
        list.Accounts.Select(info => info with { Account = info.Account with { Section = null } }).ToArray());

    /// <summary>Makes a section's account the current one and returns the merged list.</summary>
    private async Task<AccountSessionList> UseSectionAsync(ServerDefinition server, string? section, CancellationToken ct)
    {
        if (AccountSections(server).Count > 0) _sectionCache.SetActiveSection(server.Id, section);
        return await SectionAccountsAsync(server, refresh: false, ct).ConfigureAwait(false);
    }

    /// <summary>The server whose account a launch uses: the separate section of the current account, or the profile.</summary>
    private ServerDefinition LaunchAccountServer(ServerDefinition server, string? section) =>
        AccountSections(server).Count == 0 ? server : SectionServer(server, section ?? _sectionCache.ActiveSection(server.Id));

    /// <summary>The Launcher's view of one server profile's sections and servers.</summary>
    private sealed record ServerStatusView(IReadOnlyList<SectionView> Sections, IReadOnlyList<ServerView> Servers)
    {
        public static ServerStatusView Empty { get; } = new([], []);

        public static ServerStatusView From(ServerDirectory directory) => new(
            (directory.Sections ?? []).Select((section, index) => new SectionView(section.Id, section.Name, index == 0, section.ApiUrl is not null)).ToArray(),
            directory.Servers.Select(selector =>
            {
                var maintenance = selector.Targets.Select(target => target.Maintenance).FirstOrDefault(window => window is not null);
                // A server is under maintenance when a node backing it is; otherwise it is open when a node is ready.
                var state = maintenance is not null ? "maintenance"
                    : selector.Targets.Any(target => target.Online && !target.Draining) ? "online" : "offline";
                return new ServerView(selector.Id, selector.Name, selector.Section ?? "", selector.Automatic, state,
                    maintenance is null ? null : new(maintenance.Start, maintenance.End, maintenance.Description),
                    selector.Targets.Count == 0 ? null : selector.Targets.Sum(target => target.Players),
                    selector.Targets.Count == 0 ? null : selector.Targets.Sum(target => target.Capacity));
            }).ToArray());
    }

    /// <summary>A section; separate ones sign players in through their own API and keep their own accounts.</summary>
    private sealed record SectionView(string Id, string Name, bool Default, bool Separate);

    private sealed record ServerView(int Id, string Name, string Section, bool Automatic, string State, MaintenanceView? Maintenance, int? Players, int? Capacity);

    private sealed record MaintenanceView(DateTimeOffset? Start, DateTimeOffset? End, string Description);
}
