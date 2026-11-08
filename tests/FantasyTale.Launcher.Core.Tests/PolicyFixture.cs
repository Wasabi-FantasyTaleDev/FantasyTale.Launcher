using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

internal static class PolicyFixture
{
    internal static ClientPolicy Recommendations(params string[] plugins) => new(1, [], plugins.Select(plugin =>
        new ClientPluginRules(plugin, [new("offer", "recommend", [], Sources: [])])).ToArray());

    internal static ClientPolicy Rules(string plugin, params string[] types) => new(1, [],
        [new(plugin, types.Select(type => new ClientPolicyRule(type.Replace('_', '-'), type, [], Sources: type == "recommend" ? [] : null)).ToArray())]);
}
