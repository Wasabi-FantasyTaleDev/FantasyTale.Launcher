using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;
using FantasyTale.Launcher.Sdk;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>Plugin identity carried by every planned edit for provenance and conflict cards.</summary>
internal sealed record EditOwner(string PluginId, string Version, LocalizedText Name);

/// <summary>One plugin's resolved fine-grained edit, already bound to the current build and client language.</summary>
internal sealed record PlannedEdit(EditOwner Owner, ResourceTarget Target, string Archive, string Member, string Scope, string Key,
    EditAction Action, RecordEdit? Record, UiEdit? Ui)
{
    /// <summary>Owner-independent canonical content; identical content from several plugins coalesces.</summary>
    internal string Content => JsonSerializer.Serialize(new
    {
        kind = Target.Kind, shape = Record is null ? null : Target.Shape,
        Archive = Archive.ToLowerInvariant(), Member = Member.ToLowerInvariant(), Scope, Key, Action = Action.ToString(),
        set = Record?.Set.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)
            ?? Ui?.Set.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value),
        occurrences = Record?.Occurrences?.Select(o => o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)),
        expect = (Record?.Expect ?? Ui?.Expect)?.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value),
        element = Ui?.Element, position = Ui?.Position
    });

    /// <summary>Requested values per written field, used for field-level conflict detection.</summary>
    internal IReadOnlyDictionary<string, string> Fields => Record is { Occurrences: { } occurrences }
        // Per-record values compare as the sequence of every record's value; a record left unchanged reads <same>.
        ? occurrences.SelectMany(o => o.Keys).Distinct(StringComparer.Ordinal).ToDictionary(c => c,
            c => string.Join(" / ", occurrences.Select(o => o.TryGetValue(c, out var v) ? v.ToString() : "<same>")), StringComparer.Ordinal)
        : Record is not null ? Record.Set.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal)
        : Ui!.Set.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    /// <summary>Returns a copy without the given fields after a user kept another plugin's values.</summary>
    internal PlannedEdit Without(IReadOnlySet<string> fields) => Record is not null
        ? this with { Record = Record with { Set = Record.Set.Where(p => !fields.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            Occurrences = Record.Occurrences?.Select(o => (IReadOnlyDictionary<string, CellValue>)o.Where(p => !fields.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)).ToArray() } }
        : this with { Ui = Ui! with { Set = Ui.Set.Where(p => !fields.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) } };
}

/// <summary>A whole-member replacement or removal from an archive-members or character model plan.</summary>
internal sealed record WholeMemberClaim(EditOwner Owner, string Archive, string Member, string Action, object Token);

/// <summary>
/// Merges edits from all plugins independently of enumeration order. Non-overlapping and identical edits merge; any other
/// overlap becomes a structured conflict unless a choice bound to the current snapshot keeps one plugin.
/// </summary>
internal static class ResourceEditMerger
{
    internal sealed record Result(List<PlannedEdit> Accepted, List<WholeMemberClaim> DroppedWhole, List<GameResourceConflict> Conflicts, IReadOnlySet<string> RecognizedChoices);

    internal static Result Merge(IReadOnlyList<PlannedEdit> edits, IReadOnlyList<WholeMemberClaim> whole, IReadOnlyDictionary<string, string> choices)
    {
        var dropped = new HashSet<PlannedEdit>(ReferenceEqualityComparer.Instance);
        var droppedFields = new Dictionary<PlannedEdit, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var droppedWhole = new List<WholeMemberClaim>();
        var conflicts = new List<GameResourceConflict>();
        var recognized = new HashSet<string>(StringComparer.Ordinal);

        string? Choose(string id, IEnumerable<string> parties)
        {
            recognized.Add(id);
            if (!choices.TryGetValue(id, out var keep)) return null;
            if (!parties.Contains(keep)) throw new InvalidDataException("Invalid winner for resource conflict " + id);
            return keep;
        }
        static GameResourceConflictParty Party(EditOwner owner, string action, string? value) => new(owner.PluginId, owner.Version, owner.Name, action, value);

        var members = edits.Select(e => (Archive: e.Archive.ToLowerInvariant(), Member: e.Member.ToLowerInvariant()))
            .Concat(whole.Select(w => (Archive: w.Archive.ToLowerInvariant(), Member: w.Member.ToLowerInvariant()))).Distinct();
        foreach (var address in members)
        {
            var member = edits.Where(e => e.Archive.Equals(address.Archive, StringComparison.OrdinalIgnoreCase)
                && e.Member.Equals(address.Member, StringComparison.OrdinalIgnoreCase)).ToList();
            var claims = whole.Where(w => w.Archive.Equals(address.Archive, StringComparison.OrdinalIgnoreCase)
                && w.Member.Equals(address.Member, StringComparison.OrdinalIgnoreCase)).ToList();
            if (claims.Any(c => member.Any(e => e.Owner.PluginId == c.Owner.PluginId))
                || claims.GroupBy(c => c.Owner.PluginId).Any(g => g.Select(c => c.Action).Distinct().Count() > 1))
                throw new InvalidDataException(address.Member + ": a plugin makes contradictory whole-member edits");
            var first = member.FirstOrDefault();
            var archivePath = first?.Archive ?? claims[0].Archive;
            var memberPath = first?.Member ?? claims[0].Member;
            if (claims.Count > 0 && (member.Count > 0 || claims.Select(c => c.Action).Distinct().Count() > 1))
            {
                var id = Id("member-replace", archivePath, memberPath, "", "", null);
                var owners = member.Select(e => e.Owner).Concat(claims.Select(c => c.Owner)).DistinctBy(o => o.PluginId).OrderBy(o => o.PluginId, StringComparer.Ordinal).ToList();
                var keep = Choose(id, owners.Select(o => o.PluginId));
                if (keep is null)
                {
                    conflicts.Add(new(id, "resource.conflict.member-replace", archivePath, memberPath, first?.Target.Name ?? "", "", null, null,
                        owners.Select(o => Party(o, claims.Any(c => c.Owner.PluginId == o.PluginId) ? "member-replace" : "edit", null)).ToArray()));
                    continue;
                }
                if (claims.Any(c => c.Owner.PluginId == keep))
                {
                    dropped.UnionWith(member);
                    droppedWhole.AddRange(claims.Where(c => c.Owner.PluginId != keep));
                    continue;
                }
                droppedWhole.AddRange(claims);
            }
            if (first is null) continue;
            var items = member.Where(e => !dropped.Contains(e)).ToList();
            // A subtree replacement/removal would erase the same plugin's descendant writes in
            // canonical order. Reject the contradictory plan before preparing any archive output.
            foreach (var ancestor in items.Where(e => e.Ui is not null && e.Action is EditAction.Replace or EditAction.Remove))
                if (items.Any(e => e.Ui is not null && e.Owner.PluginId == ancestor.Owner.PluginId
                    && e.Key.StartsWith(ancestor.Key + "/", StringComparison.Ordinal)
                    && (ancestor.Action == EditAction.Replace || e.Action != EditAction.Remove)))
                    throw new InvalidDataException(ancestor.Owner.PluginId + ": contradictory subtree and descendant edits to " + memberPath + " " + ancestor.Key);
            foreach (var group in items.GroupBy(e => (e.Scope, e.Key)))
            {
                var ops = group.ToList();
                var occurrences = ops.Select(o => o.Record?.Occurrences?.Count ?? 1).Max();
                string Value(PlannedEdit operation, string field) => operation.Record is { } record
                    ? JsonSerializer.Serialize(Enumerable.Range(0, occurrences).Select(i => record.For(i, occurrences, operation.Member).TryGetValue(field, out var value) ? value.ToString() : null))
                    : operation.Fields[field];
                foreach (var owner in ops.GroupBy(o => o.Owner.PluginId))
                {
                    var own = owner.ToArray();
                    var patches = own.All(o => o.Action is EditAction.Patch or EditAction.Upsert);
                    if (patches ? own.SelectMany(o => o.Fields.Keys).Distinct().Any(f => own.Where(o => o.Fields.ContainsKey(f)).Select(o => Value(o, f)).Distinct().Count() > 1)
                        : own.Select(o => o.Content).Distinct().Count() > 1)
                        throw new InvalidDataException(owner.Key + ": contradictory edits to " + memberPath + " " + group.Key.Key);
                }
                if (ops.Select(o => o.Owner.PluginId).Distinct().Count() < 2) continue;
                var distinct = ops.DistinctBy(o => o.Content).ToList();
                if (distinct.Count < 2) continue;
                if (ops.All(o => o.Action is EditAction.Patch or EditAction.Upsert))
                {
                    foreach (var field in ops.SelectMany(o => o.Fields.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    {
                        var writers = ops.Where(o => o.Fields.ContainsKey(field)).ToList();
                        if (writers.Select(o => Value(o, field)).Distinct().Count() < 2) continue;
                        var id = Id("field-value", first.Archive, first.Member, group.Key.Scope, group.Key.Key, field);
                        var keep = Choose(id, writers.Select(o => o.Owner.PluginId));
                        if (keep is null)
                        {
                            conflicts.Add(new(id, "resource.conflict.field-value", first.Archive, first.Member, Scope(first, group.Key.Scope), Display(group.Key.Key), field, null,
                                writers.OrderBy(o => o.Owner.PluginId, StringComparer.Ordinal).Select(o => Party(o.Owner, o.Action.ToString().ToLowerInvariant(), Short(o.Fields[field]))).ToArray()));
                            continue;
                        }
                        foreach (var loser in writers.Where(o => o.Owner.PluginId != keep))
                        {
                            if (!droppedFields.TryGetValue(loser, out var set)) droppedFields.Add(loser, set = new(StringComparer.Ordinal));
                            set.Add(field);
                        }
                    }
                    continue;
                }
                var actions = distinct.Select(o => o.Action).ToHashSet();
                var code = actions.Contains(EditAction.Remove) ? "remove-edit" : actions.All(a => a == EditAction.Add) ? "duplicate-add"
                    : actions.Contains(EditAction.Add) ? "add-edit" : actions.Contains(EditAction.Patch) ? "row-vs-field" : "replace-replace";
                var groupId = Id(code, first.Archive, first.Member, group.Key.Scope, group.Key.Key, null);
                var winner = Choose(groupId, ops.Select(o => o.Owner.PluginId));
                if (winner is null)
                {
                    conflicts.Add(new(groupId, "resource.conflict." + code, first.Archive, first.Member, Scope(first, group.Key.Scope), Display(group.Key.Key), null, null,
                        ops.GroupBy(o => o.Owner.PluginId).OrderBy(g => g.Key, StringComparer.Ordinal)
                            .Select(g => Party(g.First().Owner, string.Join(",", g.Select(o => o.Action.ToString().ToLowerInvariant()).Distinct()), null)).ToArray()));
                    continue;
                }
                dropped.UnionWith(ops.Where(o => o.Owner.PluginId != winner));
            }
            // A newly inserted subtree owns its descendants just as a replacement does.
            foreach (var ancestor in items.Where(e => e.Ui is not null && e.Action is EditAction.Remove or EditAction.Replace or EditAction.Add && !dropped.Contains(e)).ToList())
            {
                var below = items.Where(e => e.Ui is not null && e.Owner.PluginId != ancestor.Owner.PluginId && !dropped.Contains(e)
                    && e.Key.StartsWith(ancestor.Key + "/", StringComparison.Ordinal)).ToList();
                if (ancestor.Action == EditAction.Add)
                {
                    var paths = UiDocument.AddedPaths(ancestor.Ui!).Where(p => p != ancestor.Key).ToArray();
                    below.RemoveAll(e => !paths.Any(p => e.Key == p || e.Key.StartsWith(p + "/", StringComparison.Ordinal) || p.StartsWith(e.Key + "/", StringComparison.Ordinal)));
                }
                if (below.Count == 0) continue;
                var id = Id("parent-child", first.Archive, first.Member, "ui", ancestor.Key, null);
                var parties = below.Prepend(ancestor).ToList();
                var keep = Choose(id, parties.Select(p => p.Owner.PluginId));
                if (keep is null)
                {
                    if (conflicts.Any(c => c.Id == id)) continue;
                    conflicts.Add(new(id, "resource.conflict.parent-child", first.Archive, first.Member, ancestor.Key, ancestor.Key, null, null,
                        parties.GroupBy(p => p.Owner.PluginId).OrderBy(g => g.Key, StringComparer.Ordinal)
                            .Select(g => Party(g.First().Owner, string.Join(",", g.Select(o => o.Action.ToString().ToLowerInvariant() + " " + o.Key).Distinct()), null)).ToArray()));
                    continue;
                }
                if (keep == ancestor.Owner.PluginId) dropped.UnionWith(below);
                else dropped.Add(ancestor);
            }
        }
        var accepted = new List<PlannedEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in edits.Where(e => !dropped.Contains(e)).OrderBy(e => e.Owner.PluginId, StringComparer.Ordinal))
        {
            var current = droppedFields.TryGetValue(edit, out var fields) ? edit.Without(fields) : edit;
            if (current.Action is EditAction.Patch or EditAction.Upsert && current.Fields.Count == 0) continue;
            // Identical content from several plugins is applied once; non-overlapping patches each apply.
            if (current.Action is not (EditAction.Patch or EditAction.Upsert) && !seen.Add(current.Content)) continue;
            accepted.Add(current);
        }
        return new(accepted, droppedWhole, conflicts, recognized);
    }

    private static string Scope(PlannedEdit edit, string scope) => scope.Length > 0 ? scope : edit.Target.Name;
    private static string Display(string key) => key.Replace('\u001F', '|');
    private static string? Short(string value) => value.Length <= 512 ? value : null;

    private static string Id(string code, string archive, string member, string scope, string key, string? field) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001E", code, archive.ToLowerInvariant(), member.ToLowerInvariant(), scope, key, field ?? ""))))[..24];
}

/// <summary>Applies accepted edits to one original member and returns the regenerated bytes.</summary>
internal static class ResourceEditBuilder
{
    /// <summary>Applies edits in a deterministic order so the output never depends on plugin enumeration.</summary>
    internal static byte[] Build(byte[] original, string member, IReadOnlyList<PlannedEdit> edits, CancellationToken cancellationToken)
    {
        var kinds = edits.Select(e => e.Target.Kind).Distinct().ToArray();
        if (kinds.Length != 1) throw new InvalidDataException(member + ": targets of different kinds address the same member");
        if (kinds[0] == "ui")
        {
            var form = UiDocument.Load(original, member);
            var ordered = Order(edits).ToArray();
            form.Prepare(ordered.Select(e => e.Ui!));
            foreach (var edit in ordered.Where(e => e.Action != EditAction.Remove))
            { cancellationToken.ThrowIfCancellationRequested(); form.Apply(edit.Ui! with { Expect = new Dictionary<string, string>() }); }
            form.RemoveAll(ordered.Where(e => e.Action == EditAction.Remove).Select(e => e.Ui! with { Expect = new Dictionary<string, string>() }));
            return form.Save();
        }
        IRecordDocument document = kinds[0] == "spreadsheet" ? SpreadsheetDocument.Load(original, member) : DelimitedDocument.Load(original, member);
        // Expectations describe the user's original resource, never an earlier plugin's result.
        foreach (var edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (field, expected) in edit.Record!.Expect)
            {
                var values = document.ReadAll(edit.Target.Shape, edit.Record.Key, field);
                if (values.Count == 0 || values.Any(value => (value ?? "") != expected))
                    throw new InvalidDataException(member + ": precondition failed for " + edit.Key + " " + field);
            }
        }
        foreach (var edit in Order(edits))
        { cancellationToken.ThrowIfCancellationRequested(); document.Apply(edit.Target.Shape, edit.Record! with { Expect = new Dictionary<string, string>() }); }
        return document.Save();
    }

    /// <summary>Canonical application order shared by building and cache identity.</summary>
    internal static IEnumerable<PlannedEdit> Order(IEnumerable<PlannedEdit> edits) => edits
        .OrderBy(e => Rank(e))
        .ThenBy(e => e.Action == EditAction.Remove ? -Depth(e) : Depth(e))
        .ThenBy(e => e.Scope, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal)
        .ThenBy(e => e.Ui?.Position ?? 0).ThenBy(e => e.Owner.PluginId, StringComparer.Ordinal).ThenBy(e => e.Content, StringComparer.Ordinal);

    private static int Rank(PlannedEdit edit) => edit.Ui is not null
        ? edit.Action switch { EditAction.Add => 0, EditAction.Patch => 1, EditAction.Replace => 2, EditAction.Hide => 3, _ => 4 }
        : edit.Action switch { EditAction.Remove => 0, EditAction.Replace => 1, EditAction.Patch => 2, EditAction.Upsert => 3, _ => 4 };

    private static int Depth(PlannedEdit edit) => edit.Ui is null ? 0 : edit.Key.Count(c => c == '/');
}
