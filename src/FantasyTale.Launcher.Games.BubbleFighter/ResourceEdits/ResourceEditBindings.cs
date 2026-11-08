using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Binds aliases to physical addresses before merging. Verified binding metadata avoids reopening unchanged members on
/// subsequent launches; new metadata is committed only after conflict resolution succeeds.
/// </summary>
internal sealed class ResourceEditBindings
{
    private const int Version = 1;
    private readonly List<(string Path, CacheEntry Entry)> _pending = [];

    internal List<PlannedEdit> Normalize(LaunchContext context, IReadOnlyDictionary<string, string> inputs, IEnumerable<PlannedEdit> edits)
    {
        var result = new List<PlannedEdit>();
        foreach (var archive in edits.GroupBy(e => e.Archive, StringComparer.OrdinalIgnoreCase))
        {
            IArchiveReader? reader = null;
            try
            {
                foreach (var member in archive.GroupBy(e => e.Member, StringComparer.OrdinalIgnoreCase))
                {
                    context.PreparationCancellation.ThrowIfCancellationRequested();
                    var ordered = member.OrderBy(e => e.Owner.PluginId, StringComparer.Ordinal).ThenBy(e => e.Content, StringComparer.Ordinal).ToArray();
                    if (ordered.Select(e => e.Target.Kind).Distinct(StringComparer.Ordinal).Count() != 1)
                        throw new InvalidDataException(member.Key + ": targets of different kinds address the same member");
                    var identity = Digest(JsonSerializer.Serialize(new { Version, input = inputs[archive.Key], edits = ordered.Select(e => e.Content) }));
                    var slot = Digest(JsonSerializer.Serialize(new { archive = archive.Key.ToLowerInvariant(), member = member.Key.ToLowerInvariant() }));
                    var path = context.ResourceCacheFolder is null ? null : ArchiveResourceComposer.SafeFile(context.ResourceCacheFolder, slot + ".bindings.json");
                    var bindings = Read(path, identity, ordered.Length);
                    if (bindings is null)
                    {
                        reader ??= Archive.Open(ArchiveResourceComposer.SafeFile(context.InstanceFolder, archive.Key), archive.Key);
                        var entry = reader.Entries.SingleOrDefault(e => e.Path.Equals(member.Key, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException("Resource edits address a member that is not in " + archive.Key + ": " + member.Key);
                        var original = reader.Read(entry.Path);
                        if (ordered[0].Ui is not null)
                        {
                            var form = UiDocument.Load(original, member.Key);
                            bindings = ordered.Select(e => new EditBinding(null, form.CanonicalPath(e.Key))).ToArray();
                        }
                        else
                        {
                            IRecordDocument document = ordered[0].Target.Kind == "spreadsheet"
                                ? SpreadsheetDocument.Load(original, member.Key) : DelimitedDocument.Load(original, member.Key);
                            bindings = ordered.Select(e => new EditBinding(document.Bind(e.Target.Shape,
                                e.Record!.Set.Keys.Concat(e.Record.Expect.Keys).Concat(e.Record.Occurrences?.SelectMany(o => o.Keys) ?? [])), null)).ToArray();
                        }
                        if (path is not null)
                        {
                            var payload = JsonSerializer.Serialize(bindings);
                            _pending.Add((path, new(identity, payload, Digest(payload))));
                        }
                    }
                    var bound = ordered.Select((edit, i) => Apply(edit, bindings[i])).ToArray();
                    // Different key schemas could address the same row without sharing its business key identity.
                    foreach (var scope in bound.Where(e => e.Record is not null).GroupBy(e => e.Scope, StringComparer.Ordinal))
                        if (scope.Select(e => JsonSerializer.Serialize(e.Target.Shape)).Distinct(StringComparer.Ordinal).Count() != 1)
                            throw new InvalidDataException(member.Key + ": incompatible key or header layouts for worksheet " + scope.Key);
                    result.AddRange(bound);
                }
            }
            finally { reader?.Dispose(); }
        }
        return result;
    }

    internal void Commit()
    {
        foreach (var (path, entry) in _pending)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var pending = path + ".pending";
            File.WriteAllText(pending, JsonSerializer.Serialize(entry));
            File.Move(pending, path, overwrite: true);
        }
    }

    private static PlannedEdit Apply(PlannedEdit edit, EditBinding binding)
    {
        if (edit.Ui is not null)
            return edit with { Scope = "ui", Key = binding.UiPath!, Ui = edit.Ui with { Path = binding.UiPath! } };
        var record = edit.Record!;
        var resolved = binding.Record!;
        return edit with
        {
            Scope = resolved.Scope,
            Target = edit.Target with { Sheet = resolved.Shape.Sheet, HeaderRow = resolved.Shape.HeaderRow,
                Keys = resolved.Shape.Keys, Orientation = resolved.Shape.Orientation },
            Record = record with { Set = resolved.Map(record.Set, edit.Member), Expect = resolved.Map(record.Expect, edit.Member),
                Occurrences = record.Occurrences?.Select(o => resolved.Map(o, edit.Member)).ToArray() }
        };
    }

    private static EditBinding[]? Read(string? path, string identity, int count)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path));
            if (entry?.Identity != identity || entry.Payload is null || Digest(entry.Payload) != entry.Sha256) return null;
            var bindings = JsonSerializer.Deserialize<EditBinding[]>(entry.Payload);
            return bindings?.Length == count && bindings.All(b => b is not null && (b.Record is not null || b.UiPath is not null)) ? bindings : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed record EditBinding(RecordBinding? Record, string? UiPath);
    private sealed record CacheEntry(string Identity, string Payload, string Sha256);
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
