using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Collects every fine-grained edit and whole-member claim of one preparation, binds conflict choices to an input snapshot,
/// and regenerates each edited member once so the archive composer sees a single payload per member.
/// </summary>
internal sealed class ResourceEditStage
{
    // Bump when the builders change output for the same inputs, so cached members are not reused.
    private const int BuilderVersion = 2;
    private readonly List<PlannedEdit> _edits = [];
    private readonly List<PatternEdit> _patterns = [];
    private readonly List<WholeMemberClaim> _whole = [];
    private readonly SortedDictionary<string, string> _selections = new(StringComparer.Ordinal);

    /// <summary>Whether any fine-grained edit was planned.</summary>
    internal bool HasEdits => _edits.Count > 0 || _patterns.Count > 0 || _whole.Count > 0;

    /// <summary>Client archives touched by fine-grained edits.</summary>
    internal IEnumerable<string> Archives => _edits.Select(e => e.Archive).Concat(_patterns.Select(p => p.Target.Archive)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Plans one resource-edits selection and records its identity for the snapshot.</summary>
    internal void AddSelection(LaunchContext context, GameResourceSelection selection)
    {
        var plan = ResourceEditPlanner.Load(context, selection);
        _edits.AddRange(plan.Edits);
        _patterns.AddRange(plan.Patterns);
        Identify(selection);
    }

    /// <summary>Records any selection's identity, including whole-member providers, so its changes invalidate old choices.</summary>
    internal void Identify(GameResourceSelection selection)
    {
        var package = selection.Package;
        var manifest = package.Resources is null ? "" : ArchiveResourceComposer.FileHash(ArchiveResourceComposer.SafeFile(package.Folder, package.Resources.Manifest));
        _selections[package.Id] = JsonSerializer.Serialize(new
        {
            package.Version, manifest,
            options = selection.Options.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + JsonSerializer.Serialize(p.Value))
        });
    }

    /// <summary>Records a whole-member replacement or removal; <paramref name="token"/> identifies it for removal after a choice.</summary>
    internal void Claim(PluginDefinition owner, string archive, string member, string action, string? sha256, object token) =>
        _whole.Add(new(new(owner.Id, owner.Version, owner.Name), archive, member, action + ":" + (sha256 ?? ""), token));

    /// <summary>
    /// Digest of build, client language, plugin identities, versions, options, manifests, planned edits and original archives.
    /// A conflict choice is honoured only while this digest is unchanged.
    /// </summary>
    internal string Snapshot(LaunchContext context, IReadOnlyDictionary<string, string> inputs) => Digest(JsonSerializer.Serialize(new
    {
        build = context.Build.Id, language = ResourceEditPlanner.Language(context), builder = BuilderVersion,
        selections = _selections.Select(p => p.Key + "=" + p.Value),
        edits = ResourceEditBuilder.Order(_edits).Select(e => e.Owner.PluginId + "@" + e.Owner.Version + ":" + e.Content),
        patterns = _patterns.OrderBy(p => p.Owner.PluginId, StringComparer.Ordinal).ThenBy(p => p.Order)
            .Select(p => p.Owner.PluginId + "@" + p.Owner.Version + ":" + p.Content),
        whole = _whole.Select(w => w.Owner.PluginId + "@" + w.Owner.Version + ":" + w.Archive.ToLowerInvariant() + ":" + w.Member.ToLowerInvariant() + ":" + w.Action)
            .Order(StringComparer.Ordinal),
        inputs = inputs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key.ToLowerInvariant() + "=" + p.Value)
    }));

    /// <summary>
    /// Merges all plans. Unresolved conflicts raise <see cref="GameResourceConflictException"/> before any output exists;
    /// choices from another snapshot are ignored. Returns accepted edits and the whole-member tokens a choice dropped.
    /// </summary>
    internal (IReadOnlyList<PlannedEdit> Accepted, IReadOnlySet<object> DroppedClaims) Resolve(LaunchContext context, IReadOnlyDictionary<string, string> inputs)
    {
        var snapshot = Snapshot(context, inputs);
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var choice in context.ResourceConflictChoices.Where(c => c.Snapshot == snapshot))
            if (!choices.TryAdd(choice.ConflictId, choice.KeepPluginId) && choices[choice.ConflictId] != choice.KeepPluginId)
                throw new InvalidDataException("Contradictory choices for resource conflict " + choice.ConflictId);
        // Patterns become ordinary edits first, so they merge and conflict exactly like literal edits.
        var edits = _patterns.Count == 0 ? _edits : [.. _edits, .. PatternExpander.Expand(_patterns, _edits, context.InstanceFolder, inputs,
            context.ResourceCacheFolder, context.PreparationCancellation)];
        var bindings = new ResourceEditBindings();
        var normalized = bindings.Normalize(context, inputs, edits);
        var result = ResourceEditMerger.Merge(normalized, _whole, choices);
        foreach (var id in choices.Keys)
            if (!result.RecognizedChoices.Contains(id)) throw new InvalidDataException("Unknown resource conflict " + id);
        if (result.Conflicts.Count > 0)
            throw new GameResourceConflictException(new(snapshot, context.Build.Id, ResourceEditPlanner.Language(context),
                result.Conflicts.OrderBy(c => c.Archive, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Member, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Target, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray()));
        bindings.Commit();
        return (result.Accepted, result.DroppedWhole.Select(w => w.Token).ToHashSet(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Regenerates each member of one archive that accepted edits address, reusing a verified cached member when its inputs are
    /// identical. Returns one archive edit per member plus the provenance of every contributing plugin.
    /// </summary>
    internal static List<(ArchiveResourceEdit Edit, GameResourceChange[] Changes)> Generate(string input, string archive, string inputHash,
        IEnumerable<PlannedEdit> edits, string? cache, Func<string> staged, CancellationToken cancellationToken)
    {
        var result = new List<(ArchiveResourceEdit, GameResourceChange[])>();
        IArchiveReader? reader = null;
        try
        {
            foreach (var member in edits.GroupBy(e => e.Member, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ordered = ResourceEditBuilder.Order(member).ToArray();
                var identity = Digest(JsonSerializer.Serialize(new
                {
                    builder = BuilderVersion, archive = archive.ToLowerInvariant(), input = inputHash, member = member.Key.ToLowerInvariant(),
                    edits = ordered.Select(e => e.Content)
                }));
                var slot = Digest(string.Join("|", "member", archive.ToLowerInvariant(), member.Key.ToLowerInvariant()));
                var output = cache is null ? null : ArchiveResourceComposer.SafeFile(cache, slot + ".member");
                var metadata = cache is null ? null : ArchiveResourceComposer.SafeFile(cache, slot + ".member.json");
                MemberCache? hit = null;
                if (output is not null && File.Exists(metadata) && File.Exists(output))
                {
                    try
                    {
                        var stored = JsonSerializer.Deserialize<MemberCache>(File.ReadAllText(metadata!));
                        if (stored?.Identity == identity && ArchiveResourceComposer.FileHash(output) == stored.Sha256) hit = stored;
                    }
                    catch (JsonException) { /* Interrupted cache metadata is disposable; the original archive stays authoritative. */ }
                }
                if (hit is null)
                {
                    reader ??= Archive.Open(input, archive);
                    var entry = reader.Entries.SingleOrDefault(e => e.Path.Equals(member.Key, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("Resource edits address a member that is not in " + archive + ": " + member.Key);
                    var original = reader.Read(entry.Path);
                    var bytes = ResourceEditBuilder.Build(original, entry.Path, ordered, cancellationToken);
                    var temporary = staged();
                    File.WriteAllBytes(temporary, bytes);
                    hit = new(identity, entry.Path, Convert.ToHexStringLower(SHA256.HashData(bytes)), Convert.ToHexStringLower(SHA256.HashData(original)));
                    if (output is null) output = temporary;
                    else
                    {
                        Directory.CreateDirectory(cache!);
                        File.Move(temporary, output, overwrite: true);
                        var pending = ArchiveResourceComposer.SafeFile(cache!, slot + ".member.pending");
                        File.WriteAllText(pending, JsonSerializer.Serialize(hit));
                        File.Move(pending, metadata!, overwrite: true);
                    }
                }
                var changes = ordered.GroupBy(e => e.Owner.PluginId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new GameResourceChange(g.Key, g.First().Owner.Version, g.First().Owner.Name, hit.Path, "edit", hit.OriginalSha256, hit.Sha256))
                    .ToArray();
                result.Add((new(hit.Path, output, hit.Sha256, hit.OriginalSha256), changes));
            }
            return result;
        }
        finally { reader?.Dispose(); }
    }

    private sealed record MemberCache(string Identity, string Path, string Sha256, string OriginalSha256);

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
