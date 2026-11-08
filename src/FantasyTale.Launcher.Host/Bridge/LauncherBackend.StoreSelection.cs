using System.Text.Json.Nodes;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private StoreSource PluginSource(JsonNode? parameters)
    {
        var source = _sources.List().SingleOrDefault(value => value.Id == Text(parameters, "sourceId"))
            ?? throw new ArgumentException("Unknown source.");
        if (!source.Enabled || source.BaseUrl is null) throw new InvalidOperationException("Configure and enable this source first.");
        if (parameters?["expectedSource"] is { } expected)
        {
            Keys(expected, "kind", "baseUrl", "catalogPath");
            if (source.Kind != Text(expected, "kind")
                || SourceAddress.BaseUrl(source.BaseUrl) != SourceAddress.BaseUrl(Text(expected, "baseUrl"))
                || SourceAddress.RelativePath(source.CatalogPath) != SourceAddress.RelativePath(Text(expected, "catalogPath")))
                throw new InvalidOperationException("The download source changed. Refresh the store and select it again.");
        }
        return source;
    }
}
