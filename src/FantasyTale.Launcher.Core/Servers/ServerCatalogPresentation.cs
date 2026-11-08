using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

internal static class ServerCatalogPresentation
{
    internal static ServerPresentation? Merge(ServerPresentation? local, ServerPresentation? remote, JsonElement catalog)
    {
        if (local is null || remote is null) return remote ?? local;
        var row = Property(catalog, "presentation");
        bool Has(string name) => Property(row, name).ValueKind != JsonValueKind.Undefined;
        return remote with
        {
            Motd = Has("motd") ? remote.Motd : local.Motd,
            Summary = Has("summary") ? remote.Summary : local.Summary,
            IconUrl = Has("iconUrl") ? remote.IconUrl : local.IconUrl,
            Images = Has("images") ? remote.Images : local.Images,
            MotdTranslations = Translations(local.MotdTranslations, remote.MotdTranslations),
            SummaryTranslations = Translations(local.SummaryTranslations, remote.SummaryTranslations),
            IconUrlTranslations = Translations(local.IconUrlTranslations, remote.IconUrlTranslations),
            ImagesTranslations = Translations(local.ImagesTranslations, remote.ImagesTranslations),
            OnlinePlayers = Has("onlinePlayers") ? remote.OnlinePlayers : local.OnlinePlayers,
            PeakOnlinePlayers = Has("peakOnlinePlayers") ? remote.PeakOnlinePlayers : local.PeakOnlinePlayers,
            PlayerLimit = Has("playerLimit") ? remote.PlayerLimit : local.PlayerLimit,
            State = Has("state") ? remote.State : local.State,
            Whitelist = Has("whitelist") ? remote.Whitelist : local.Whitelist,
            RosterEnabled = Has("rosterEnabled") ? remote.RosterEnabled : local.RosterEnabled,
        };
    }

    private static JsonElement Property(JsonElement row, string name)
    {
        var result = default(JsonElement);
        if (row.ValueKind == JsonValueKind.Object)
            foreach (var property in row.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) result = property.Value;
        return result;
    }

    private static Dictionary<string, T> Translations<T>(IReadOnlyDictionary<string, T> local, IReadOnlyDictionary<string, T> remote)
    {
        var result = new Dictionary<string, T>(local, StringComparer.OrdinalIgnoreCase);
        foreach (var (language, value) in remote) result[language] = value;
        return result;
    }
}
