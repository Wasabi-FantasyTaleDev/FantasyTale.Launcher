using System.Text.Json.Nodes;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

/// <summary>Strictly reads conflict decisions without truncating a language pack's field-level conflict list.</summary>
internal static class ResourceConflictBridge
{
    internal static IReadOnlyList<GameResourceConflictChoice> Parse(JsonNode? value)
    {
        if (value is null) return [];
        if (value is not JsonArray choices) throw new ArgumentException("Resource conflict choices must be an array.");
        return choices.Select(node =>
        {
            if (node is not JsonObject row || row.Count != 3 || row.Any(p => p.Key is not ("snapshot" or "conflictId" or "keepPluginId")))
                throw new ArgumentException("Invalid resource conflict choice.");
            string Text(string key) => row[key] is JsonValue field && field.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
                ? text : throw new ArgumentException("Invalid resource conflict choice " + key + ".");
            return new GameResourceConflictChoice(Text("snapshot"), Text("conflictId"), Text("keepPluginId"));
        }).ToArray();
    }
}
