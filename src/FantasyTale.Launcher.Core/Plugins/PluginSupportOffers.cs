using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Clients an upgraded package newly supports and that have not been asked whether to enable it.</summary>
/// <param name="Game">The game.</param>
/// <param name="Plugin">The package.</param>
/// <param name="Version">The installed version that added the support.</param>
/// <param name="Instances">The clients to ask about.</param>
public sealed record PluginSupportOffer(string Game, string Plugin, string Version, IReadOnlyList<string> Instances);

/// <summary>
/// Remembers, across restarts, the clients an upgrade newly supports, so the user can choose to enable the package
/// there. A fresh installation is enabled by its own installation choice; existing selections keep their state.
/// Holds at most one entry per package, and answering removes the clients it covers.
/// </summary>
public sealed class PluginSupportOffers
{
    private readonly string _path;
    private readonly object _gate = new();
    private List<PluginSupportOffer> _offers = [];

    /// <summary>Opens the offers file in <paramref name="root"/>; an unreadable file starts empty rather than blocking plugins.</summary>
    public PluginSupportOffers(string root)
    {
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "plugin-support-offers.json");
        InstanceStore.RejectLinks(_path);
        try
        {
            if (File.Exists(_path)) _offers = JsonSerializer.Deserialize<List<PluginSupportOffer>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { _offers = []; }
    }

    /// <summary>The outstanding offers.</summary>
    public IReadOnlyList<PluginSupportOffer> List() { lock (_gate) return _offers.ToArray(); }

    /// <summary>
    /// Records the clients <paramref name="installed"/> supports that none of the <paramref name="previous"/> versions did
    /// and that have no selection of it yet. Returns whether an offer was added or extended.
    /// </summary>
    public bool Record(PluginDefinition installed, IReadOnlyList<PluginDefinition> previous, GameDefinition game,
        IReadOnlyList<InstanceDefinition> instances)
    {
        var added = NewlySupported(installed, previous, game, instances);
        if (added.Count == 0) return false;
        lock (_gate)
        {
            var prior = _offers.SingleOrDefault(offer => offer.Game == installed.Game && offer.Plugin == installed.Id);
            var merged = (prior?.Instances ?? []).Concat(added).Distinct(StringComparer.Ordinal).ToArray();
            var next = _offers.Where(offer => offer != prior).Append(new PluginSupportOffer(installed.Game, installed.Id, installed.Version, merged)).ToList();
            Save(next);
            return true;
        }
    }

    /// <summary>Removes answered clients, and any client that no longer exists, from a package's offer.</summary>
    public void Resolve(string game, string plugin, IReadOnlyCollection<string> answered, IReadOnlyCollection<string> existing)
    {
        lock (_gate)
        {
            var next = _offers.Select(offer => offer.Game == game && offer.Plugin == plugin
                    ? offer with { Instances = offer.Instances.Where(id => !answered.Contains(id) && existing.Contains(id)).ToArray() } : offer)
                .Where(offer => offer.Instances.Count > 0).ToList();
            Save(next);
        }
    }

    /// <summary>Clients whose build <paramref name="installed"/> supports but no previous version did, without a selection of it.</summary>
    public static IReadOnlyList<string> NewlySupported(PluginDefinition installed, IReadOnlyList<PluginDefinition> previous, GameDefinition game,
        IReadOnlyList<InstanceDefinition> instances)
    {
        // Without an earlier version this is a fresh installation, which its own enable choice covers.
        if (previous.Count == 0) return [];
        return instances.Where(instance => instance.Game == game.Id
                && game.Builds.SingleOrDefault(build => build.Id == instance.Build) is { } build
                && PluginCatalog.IsAvailable(installed, build) && !previous.Any(version => PluginCatalog.IsAvailable(version, build))
                && !instance.Plugins.Any(selection => PluginCatalog.MatchesIdentity(installed, selection.Id)))
            .Select(instance => instance.Id).ToArray();
    }

    private void Save(List<PluginSupportOffer> next)
    {
        var temporary = _path + ".tmp";
        InstanceStore.RejectLinks(temporary);
        File.WriteAllText(temporary, JsonSerializer.Serialize(next));
        File.Move(temporary, _path, true);
        _offers = next;
    }
}
