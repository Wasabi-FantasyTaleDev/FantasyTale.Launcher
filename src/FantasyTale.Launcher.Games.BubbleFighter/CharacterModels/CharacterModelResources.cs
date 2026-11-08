using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;

internal sealed class CharacterModelResources(CharacterModelCapabilities capabilities)
{
    private static readonly CharacterModelChoice[] Generations = [CharacterModelChoice.New, CharacterModelChoice.Classic];
    private static readonly string[] OriginalChoice = ["original"];
    private const string Provider = "character-models";

    internal IReadOnlyDictionary<string, object> Settings(LaunchContext context, PluginDefinition package)
    {
        if (package.Resources?.Provider is "archive-members" or ResourceEditPlanner.Provider) return new Dictionary<string, object>();
        var (_, characters) = Discover(context, package);
        return new Dictionary<string, object>
        {
            ["characters"] = characters.Select(c => new { key = c.Key, name = c.Name.Values,
                nativeGeneration = Name(c.NativeGeneration), choices = OriginalChoice.Concat(
                    Generations.Where(g => g == c.NativeGeneration || c.Plans.ContainsKey(g)).Select(Name)).ToArray() }).ToArray()
        };
    }

    internal void Validate(LaunchContext context, GameResourceSelection selection)
    {
        if (selection.Package.Resources?.Provider == "archive-members")
        {
            _ = GenericPlans(context, selection).ToArray();
            return;
        }
        if (selection.Package.Resources?.Provider == ResourceEditPlanner.Provider)
        {
            _ = ResourceEditPlanner.Load(context, selection);
            return;
        }
        var (_, characters) = Discover(context, selection.Package);
        _ = Resolve(characters, Choices(selection.Options), 0);
    }

    internal IPreparedGameResources Prepare(LaunchContext context, IReadOnlyList<GameResourceSelection> selections)
    {
        var staged = new Staged();
        var elapsed = Stopwatch.StartNew();
        try
        {
            var archives = new Dictionary<string, List<ArchiveResourceEdit>>(StringComparer.OrdinalIgnoreCase);
            var inputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var creation = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var claims = new Dictionary<string, List<GameResourceChange>>(StringComparer.OrdinalIgnoreCase);
            var resolved = new List<CharacterModelSelection>();
            var revision = capabilities.Providers(context.InstanceFolder).Revision;
            var fine = new ResourceEditStage();
            // Whole-member edits keep their contribution so a conflict choice can drop both together.
            var tokens = new Dictionary<ArchiveResourceEdit, (string Archive, GameResourceChange Change)>(ReferenceEqualityComparer.Instance);
            foreach (var selection in selections)
            {
                if (selection.Package.Resources?.Provider == ResourceEditPlanner.Provider)
                {
                    fine.AddSelection(context, selection);
                    continue;
                }
                fine.Identify(selection);
                if (selection.Package.Resources?.Provider == "archive-members")
                {
                    foreach (var reference in GenericPlans(context, selection)) ReadPlan(selection.Package, selection.Package.Folder, reference);
                    continue;
                }
                var (current, characters) = Discover(context, selection.Package);
                if (current != revision) throw new InvalidOperationException("Character providers changed. Retry launch.");
                var choices = Choices(selection.Options);
                var snapshot = Resolve(characters, choices, revision);
                resolved.AddRange(snapshot);
                foreach (var item in snapshot.Where(s => s.ChangesResources))
                {
                    var character = characters.Single(c => c.Key == item.Key);
                    ReadPlan(selection.Package, character.PackageFolder, character.Plans[item.Effective]);
                }
            }
            void ReadPlan(PluginDefinition owner, string packageFolder, string reference)
            {
                    var path = ArchiveResourceComposer.SafeFile(packageFolder, reference);
                    var plan = ResourceData.Load(path);
                    plan.Keys("schema", "archives"); plan.Schema();
                    foreach (var archive in plan.Rows("archives"))
                    {
                        archive.Keys("path", "members", "create");
                        var relative = ArchiveResourceComposer.ValidatePath(archive.Text("path"));
                        var input = ArchiveResourceComposer.SafeFile(context.InstanceFolder, relative);
                        var create = archive.Flag("create");
                        if (creation.TryGetValue(relative, out var prior) && prior != create)
                            throw new InvalidDataException("Resource plans disagree about whether the archive must exist: " + relative);
                        creation[relative] = create;
                        if (create && (!relative.EndsWith(".bfz", StringComparison.OrdinalIgnoreCase) || Path.Exists(input)))
                            throw new InvalidDataException("Archive creation requires an absent BFZ file: " + relative);
                        context.PreparationCancellation.ThrowIfCancellationRequested();
                        if (!inputHashes.ContainsKey(relative)) inputHashes.Add(relative, create ? "" : ArchiveResourceComposer.FileHash(input));
                        if (!archives.TryGetValue(relative, out var edits)) archives.Add(relative, edits = []);
                        if (!claims.TryGetValue(relative, out var contributions)) claims.Add(relative, contributions = []);
                        foreach (var member in archive.Rows("members"))
                        {
                            member.Keys("path", "source", "sha256", "original_sha256", "remove");
                            var remove = member.Flag("remove");
                            var original = member.Text("original_sha256", "");
                            if (create && (remove || original.Length != 0))
                                throw new InvalidDataException("A new archive can only contain member additions: " + relative);
                            if (remove && original.Length == 0) throw new InvalidDataException("Resource removal requires the original member digest.");
                            var edit = new ArchiveResourceEdit(member.Text("path"), remove ? null : ArchiveResourceComposer.SafeFile(packageFolder, member.Text("source")),
                                remove ? "" : ResourceData.Hash(member.Text("sha256")), original.Length == 0 ? null : ResourceData.Hash(original));
                            var change = new GameResourceChange(owner.Id, owner.Version, owner.Name, member.Text("path"),
                                remove ? "remove" : original.Length == 0 ? "add" : "replace", original.Length == 0 ? null : original,
                                remove ? null : member.Text("sha256"));
                            edits.Add(edit);
                            contributions.Add(change);
                            tokens.Add(edit, (relative, change));
                            fine.Claim(owner, relative, edit.Path, change.Action, edit.Sha256, edit);
                        }
                    }
            }
            // Fine-grained edits merge with each other and with whole-member claims before any archive is composed.
            var accepted = new Dictionary<string, List<PlannedEdit>>(StringComparer.OrdinalIgnoreCase);
            if (fine.HasEdits)
            {
                foreach (var relative in fine.Archives)
                {
                    if (creation.GetValueOrDefault(relative))
                        throw new InvalidDataException("Field-level resource edits require an existing archive: " + relative);
                    context.PreparationCancellation.ThrowIfCancellationRequested();
                    if (!inputHashes.ContainsKey(relative))
                        inputHashes.Add(relative, ArchiveResourceComposer.FileHash(ArchiveResourceComposer.SafeFile(context.InstanceFolder, relative)));
                    if (!archives.ContainsKey(relative)) archives.Add(relative, []);
                    if (!claims.ContainsKey(relative)) claims.Add(relative, []);
                }
                var (kept, dropped) = fine.Resolve(context, inputHashes);
                foreach (var token in dropped)
                {
                    var edit = (ArchiveResourceEdit)token;
                    var (relative, change) = tokens[edit];
                    archives[relative].RemoveAll(e => ReferenceEquals(e, edit));
                    claims[relative].RemoveAll(c => ReferenceEquals(c, change));
                }
                foreach (var edit in kept)
                {
                    if (!accepted.TryGetValue(edit.Archive, out var list)) accepted.Add(edit.Archive, list = []);
                    list.Add(edit);
                }
                foreach (var relative in archives.Keys.Where(k => archives[k].Count == 0 && !accepted.ContainsKey(k)).ToArray())
                { archives.Remove(relative); claims.Remove(relative); }
            }
            var completedArchives = 0;
            var rebuiltArchives = 0;
            var progressGate = new object();
            var fractions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            void Report(string relative, long done, long total, bool finished = false)
            {
                lock (progressGate)
                {
                    fractions[relative] = finished ? 1 : total > 0 ? (double)done / total : 0;
                    if (finished) completedArchives++;
                    context.ResourceProgress?.Report(new(relative, done, total, "building", elapsed.Elapsed.TotalSeconds)
                    {
                        CompletedArchives = completedArchives, TotalArchives = archives.Count,
                        Percentage = archives.Count == 0 ? 100 : fractions.Values.Sum() / archives.Count * 100
                    });
                }
            }
            Parallel.ForEach(archives, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount), CancellationToken = context.PreparationCancellation }, pair =>
            {
                var (relative, edits) = pair;
                context.PreparationCancellation.ThrowIfCancellationRequested();
                var input = ArchiveResourceComposer.SafeFile(context.InstanceFolder, relative);
                var create = creation.GetValueOrDefault(relative);
                if (accepted.TryGetValue(relative, out var planned))
                    foreach (var (edit, changes) in ResourceEditStage.Generate(input, relative, inputHashes[relative], planned,
                        context.ResourceCacheFolder, staged.Output, context.PreparationCancellation))
                    {
                        edits.Add(edit);
                        claims[relative].AddRange(changes);
                    }
                var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    composer = 1, input = inputHashes[relative], path = relative, create,
                    members = edits.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => new { e.Path, e.Sha256, e.ExpectedOriginal, remove = e.Source is null })
                }))));
                var cache = context.ResourceCacheFolder;
                var slot = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relative.ToLowerInvariant())));
                var output = cache is null ? staged.Output() : ArchiveResourceComposer.SafeFile(cache, slot + ".archive");
                var metadata = cache is null ? null : ArchiveResourceComposer.SafeFile(cache, slot + ".json");
                var reused = false;
                if (metadata is not null && File.Exists(metadata) && File.Exists(output))
                {
                    try
                    {
                        var stored = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(metadata));
                        reused = stored?.Identity == identity && ArchiveResourceComposer.FileHash(output) == stored.Sha256;
                    }
                    catch (JsonException) { /* Interrupted cache metadata is disposable; originals remain authoritative. */ }
                }
                if (!reused)
                {
                    Interlocked.Increment(ref rebuiltArchives);
                    var temporary = staged.Output();
                    var reported = Stopwatch.StartNew();
                    Report(relative, 0, 0);
                    void OnProgress(long done, long total)
                    {
                        if (reported.ElapsedMilliseconds < 80 && done != total) return;
                        reported.Restart();
                        Report(relative, done, total);
                    }
                    var composer = new ArchiveResourceComposer();
                    if (create) composer.Create(relative, temporary, edits, OnProgress, context.PreparationCancellation);
                    else composer.Compose(input, relative, temporary, edits, OnProgress, context.PreparationCancellation);
                    if (cache is null) output = temporary;
                    else
                    {
                        Directory.CreateDirectory(cache);
                        var hash = ArchiveResourceComposer.FileHash(temporary);
                        File.Move(temporary, output, overwrite: true);
                        var pending = ArchiveResourceComposer.SafeFile(cache, slot + ".pending");
                        File.WriteAllText(pending, JsonSerializer.Serialize(new CacheEntry(identity, hash)));
                        File.Move(pending, metadata!, overwrite: true);
                    }
                }
                if (create ? Path.Exists(input) : ArchiveResourceComposer.FileHash(input) != inputHashes[relative])
                    throw new IOException("A client archive changed during resource preparation: " + relative);
                staged.Add(new(output, relative, new FileInfo(output).Length, ArchiveResourceComposer.FileHash(output))
                { OriginalSha256 = create ? null : inputHashes[relative], ExpectedAbsent = create,
                    Changes = claims[relative].ToArray(), CacheReused = reused });
                // Cache-only launches do not need a progress dialog.
                if (!reused) Report(relative, 1, 1, finished: true);
                else lock (progressGate) { completedArchives++; fractions[relative] = 1; }
            });
            // A cached archive may finish after the final rebuilt one reports its completion.
            if (rebuiltArchives > 0)
                context.ResourceProgress?.Report(new("", 1, 1, "complete", elapsed.Elapsed.TotalSeconds)
                { CompletedArchives = archives.Count, TotalArchives = archives.Count, Percentage = 100 });
            capabilities.Publish(context.InstanceFolder, revision, resolved);
            return staged;
        }
        catch (AggregateException error) when (error.Flatten().InnerExceptions.Count == 1)
        {
            staged.Dispose();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.Flatten().InnerExceptions[0]).Throw();
            throw;
        }
        catch { staged.Dispose(); throw; }
    }

    private static IEnumerable<string> GenericPlans(LaunchContext context, GameResourceSelection selection)
    {
        var resource = selection.Package.Resources!;
        var data = ResourceData.Load(ArchiveResourceComposer.SafeFile(selection.Package.Folder, resource.Manifest));
        data.Keys("schema", "plans"); data.Schema();
        foreach (var row in data.Rows("plans"))
        {
            row.Keys("build", "when", "when_value", "when_options", "path");
            var build = row.Text("build", "*");
            if (build != "*" && build != context.Build.Id) continue;
            var when = row.Text("when", "");
            var applies = true;
            if (row.Has("when_value"))
            {
                if (when.Length == 0)
                    throw new InvalidDataException(row.Source + ": when_value requires an option key in when.");
                var expected = row.Text("when_value");
                var option = selection.Package.Options.SingleOrDefault(option => option.Key == when);
                if (option is null || option.Type is not ("string" or "enum"))
                    throw new InvalidDataException(row.Source + ": when_value requires a declared string or enum option: " + when);
                if (option.Type == "enum" && !option.Values.Contains(expected, StringComparer.Ordinal)
                    || option.Type == "string" && Encoding.UTF8.GetByteCount(expected) > option.MaxLength)
                    throw new InvalidDataException(row.Source + ": when_value violates the option constraints: " + when);
                if (!selection.Options.TryGetValue(when, out var selected) || selected is not string text)
                    throw new InvalidDataException(row.Source + ": missing or invalid string resource option: " + when);
                if (option.Type == "enum" && !option.Values.Contains(text, StringComparer.Ordinal)
                    || option.Type == "string" && Encoding.UTF8.GetByteCount(text) > option.MaxLength)
                    throw new InvalidDataException(row.Source + ": resource option violates its declared constraints: " + when);
                applies = string.Equals(text, expected, StringComparison.Ordinal);
            }
            else if (when.Length > 0)
            {
                if (!selection.Options.TryGetValue(when, out var value) || value is not bool enabled)
                    throw new InvalidDataException("Resource plan gates require a declared boolean option: " + when);
                applies = enabled;
            }
            if (row.Table("when_options") is { } conditions)
                foreach (var key in conditions.Names)
                {
                    var option = selection.Package.Options.SingleOrDefault(option => option.Key == key)
                        ?? throw new InvalidDataException(row.Source + ": undeclared resource condition: " + key);
                    var expected = conditions.Raw(key);
                    var valid = expected switch
                    {
                        bool => option.Type == "bool",
                        string text => option.Type == "enum" && option.Values.Contains(text, StringComparer.Ordinal)
                            || option.Type == "string" && Encoding.UTF8.GetByteCount(text) <= option.MaxLength,
                        _ => false,
                    };
                    if (!valid) throw new InvalidDataException(row.Source + ": resource condition violates the option constraints: " + key);
                    if (!selection.Options.TryGetValue(key, out var selected) || selected is null || selected.GetType() != expected!.GetType())
                        throw new InvalidDataException(row.Source + ": missing or invalid resource condition option: " + key);
                    if (selected is string value && (option.Type == "enum" && !option.Values.Contains(value, StringComparer.Ordinal)
                        || option.Type == "string" && Encoding.UTF8.GetByteCount(value) > option.MaxLength))
                        throw new InvalidDataException(row.Source + ": resource condition option violates its declared constraints: " + key);
                    applies &= Equals(selected, expected);
                }
            if (!applies) continue;
            yield return ArchiveResourceComposer.ValidatePath(row.Text("path"));
        }
    }

    private (long Revision, CharacterModelDescriptor[] Characters) Discover(LaunchContext context, PluginDefinition package)
    {
        if (package.Resources?.Provider != Provider) throw new InvalidDataException("Unknown BubbleFighter resource provider: " + package.Resources?.Provider);
        var data = ResourceData.Load(ArchiveResourceComposer.SafeFile(package.Folder, package.Resources.Manifest));
        data.Keys("schema", "characters", "native"); data.Schema();
        var definitions = new Dictionary<string, ResourceData>(StringComparer.Ordinal);
        foreach (var row in data.Rows("characters"))
        {
            row.Keys("key", "name");
            if (!definitions.TryAdd(row.Text("key"), row)) throw new InvalidDataException("Duplicate character definition.");
        }
        var result = new Dictionary<string, CharacterModelDescriptor>(StringComparer.Ordinal);
        foreach (var row in data.Rows("native").Where(row => row.Text("build") == context.Build.Id))
        {
            row.Keys("build", "key", "generation", "new", "classic");
            var key = row.Text("key");
            if (!definitions.TryGetValue(key, out var definition)) throw new InvalidDataException("Undefined native character: " + key);
            var plans = new Dictionary<CharacterModelChoice, string>();
            foreach (var generation in Generations)
                if (row.Text(Name(generation), "") is { Length: > 0 } plan) plans.Add(generation, ArchiveResourceComposer.ValidatePath(plan));
            var native = Parse(row.Text("generation"));
            if (native == CharacterModelChoice.Original) throw new InvalidDataException("A native generation must be new or classic.");
            if (!result.TryAdd(key, new(key, new LocalizedText(definition.Labels("name")), native, "client", plans, package.Folder)))
                throw new InvalidDataException("Duplicate native character: " + key);
        }
        var providers = capabilities.Providers(context.InstanceFolder);
        foreach (var character in providers.Characters)
            if (!result.TryAdd(character.Key, character)) throw new InvalidDataException("Provider collides with a native character: " + character.Key);
        return (providers.Revision, result.Values.ToArray());
    }

    private static CharacterModelSelection[] Resolve(CharacterModelDescriptor[] characters, IReadOnlyDictionary<string, CharacterModelChoice> choices, long revision)
    {
        // Retain dormant choices when a provider is temporarily absent; never apply them to another identity.
        return characters.Select(character =>
        {
            var requested = choices.GetValueOrDefault(character.Key, CharacterModelChoice.Original);
            var effective = requested == CharacterModelChoice.Original ? character.NativeGeneration : requested;
            if (effective != character.NativeGeneration && !character.Plans.ContainsKey(effective))
                throw new InvalidDataException("The requested model generation is unavailable for " + character.Key);
            return new CharacterModelSelection(character.Key, requested, effective, character.Provider, effective != character.NativeGeneration, revision);
        }).ToArray();
    }

    private static Dictionary<string, CharacterModelChoice> Choices(IReadOnlyDictionary<string, object> options)
    {
        if (!options.TryGetValue("models", out var value) || value is not string text) throw new InvalidDataException("Missing models setting.");
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Model choices must be an object.");
        var result = new Dictionary<string, CharacterModelChoice>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (item.Name.Length == 0 || !item.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                throw new InvalidDataException("Invalid character identity.");
            if (!result.TryAdd(item.Name, Parse(item.Value.GetString() ?? ""))) throw new InvalidDataException("Duplicate character choice.");
        }
        return result;
    }

    private static CharacterModelChoice Parse(string value) => value switch
    {
        "original" => CharacterModelChoice.Original, "new" => CharacterModelChoice.New, "classic" => CharacterModelChoice.Classic,
        _ => throw new InvalidDataException("Unknown model choice: " + value)
    };
    private static string Name(CharacterModelChoice value) => value.ToString().ToLowerInvariant();

    private sealed record CacheEntry(string Identity, string Sha256);

    private sealed class Staged : IPreparedGameResources
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "fantasytale-resources-" + Guid.NewGuid().ToString("N"));
        private readonly List<PreparedGameResource> _files = [];
        public IReadOnlyList<PreparedGameResource> Files => _files.OrderBy(f => f.Target, StringComparer.OrdinalIgnoreCase).ToArray();
        internal string Output() { Directory.CreateDirectory(_folder); return Path.Combine(_folder, Guid.NewGuid().ToString("N")); }
        internal void Add(PreparedGameResource file) { lock (_files) _files.Add(file); }
        public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
    }
}
