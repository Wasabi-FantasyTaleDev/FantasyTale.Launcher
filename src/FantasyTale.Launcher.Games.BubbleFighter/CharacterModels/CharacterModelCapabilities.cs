using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;

/// <summary>Player preference; Original preserves the native or importing provider's resources.</summary>
public enum CharacterModelChoice
{
    /// <summary>Retain the client's or importing provider's default.</summary>
    Original,
    /// <summary>Request the remade model with its matching resource dependencies.</summary>
    New,
    /// <summary>Request the classic model with its matching resource dependencies.</summary>
    Classic
}

/// <summary>A character supplied by the client or an active expansion provider.</summary>
/// <param name="Key">Stable namespaced identity, independent of translated labels.</param>
/// <param name="Name">Official regional names, with a Latin fallback.</param>
/// <param name="NativeGeneration">The model used without an override.</param>
/// <param name="Provider">Identity of the client catalog or expansion provider.</param>
/// <param name="Plans">New/Classic plan paths relative to PackageFolder; absent entries are unavailable.</param>
/// <param name="PackageFolder">Verified provider package containing its plans and payloads.</param>
public sealed record CharacterModelDescriptor(string Key, LocalizedText Name, CharacterModelChoice NativeGeneration,
    string Provider, IReadOnlyDictionary<CharacterModelChoice, string> Plans, string PackageFolder);

/// <summary>Requested and resolved choices from one immutable preparation snapshot.</summary>
public sealed record CharacterModelSelection(string Key, CharacterModelChoice Requested, CharacterModelChoice Effective,
    string Provider, bool ChangesResources, long Revision);

/// <summary>Public capability consumed by future character and item providers without reading private settings files.</summary>
public interface ICharacterModelCapabilities
{
    /// <summary>Registers imported characters for one client; disposing the lease withdraws their capabilities.</summary>
    IDisposable Register(string clientFolder, IReadOnlyList<CharacterModelDescriptor> characters);

    /// <summary>Returns the most recent resolved launch snapshot, or no override before preparation.</summary>
    IReadOnlyList<CharacterModelSelection> Read(string clientFolder);

    /// <summary>Subscribes to provider or selection revisions; disposing the lease removes the handler.</summary>
    IDisposable Subscribe(Action<string> changed);
}

internal sealed class CharacterModelCapabilities : ICharacterModelCapabilities
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<Guid, CharacterModelDescriptor[]>> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterModelSelection[]> _selections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Action<string>> _listeners = [];
    // Each client folder has its own provider revision, so registering characters for one instance never invalidates a
    // preparation of another; values come from one counter and never repeat.
    private readonly Dictionary<string, long> _revisions = new(StringComparer.OrdinalIgnoreCase);
    private long _revision;
    private long _selectionRevision;

    public IDisposable Register(string clientFolder, IReadOnlyList<CharacterModelDescriptor> characters)
    {
        var folder = Path.GetFullPath(clientFolder);
        var copy = characters.Select(c => c with
        {
            Plans = new System.Collections.ObjectModel.ReadOnlyDictionary<CharacterModelChoice, string>(c.Plans.ToDictionary()),
            Name = new LocalizedText(new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(c.Name.Values.ToDictionary()))
        }).ToArray();
        foreach (var character in copy)
        {
            if (character.Key.Length == 0 || !character.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
                || character.NativeGeneration is not (CharacterModelChoice.New or CharacterModelChoice.Classic)
                || string.IsNullOrWhiteSpace(character.Provider) || !Directory.Exists(character.PackageFolder))
                throw new InvalidDataException("Invalid imported character descriptor.");
            foreach (var (generation, plan) in character.Plans)
            {
                if (generation is not (CharacterModelChoice.New or CharacterModelChoice.Classic)) throw new InvalidDataException("Invalid provider model generation.");
                _ = Archives.ArchiveResourceComposer.ValidatePath(plan);
            }
        }
        if (copy.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new InvalidDataException("Duplicate imported character identity.");
        Guid id;
        lock (_gate)
        {
            if (!_providers.TryGetValue(folder, out var providers)) _providers.Add(folder, providers = []);
            var existing = providers.Values.SelectMany(c => c).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            if (copy.Any(c => existing.Contains(c.Key))) throw new InvalidDataException("Character identity is already registered.");
            id = Guid.NewGuid();
            providers.Add(id, copy);
            _selections.Remove(folder);
            _revisions[folder] = ++_revision;
        }
        Notify(folder);
        return new Lease(() =>
        {
            lock (_gate) { _providers[folder].Remove(id); _selections.Remove(folder); _revisions[folder] = ++_revision; }
            Notify(folder);
        });
    }

    public IReadOnlyList<CharacterModelSelection> Read(string clientFolder)
    {
        lock (_gate) return _selections.TryGetValue(Path.GetFullPath(clientFolder), out var values) ? Array.AsReadOnly(values) : [];
    }

    public IDisposable Subscribe(Action<string> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        lock (_gate)
        {
            var id = Guid.NewGuid(); _listeners.Add(id, changed);
            return new Lease(() => { lock (_gate) _listeners.Remove(id); });
        }
    }

    internal (long Revision, CharacterModelDescriptor[] Characters) Providers(string folder)
    {
        var full = Path.GetFullPath(folder);
        lock (_gate) return (_revisions.GetValueOrDefault(full), _providers.GetValueOrDefault(full)?.Values.SelectMany(c => c).ToArray() ?? []);
    }

    internal void Publish(string folder, long expected, IEnumerable<CharacterModelSelection> values)
    {
        lock (_gate)
        {
            if (expected != _revisions.GetValueOrDefault(Path.GetFullPath(folder))) throw new InvalidOperationException("Character providers changed during resource preparation. Retry launch.");
            var revision = ++_selectionRevision;
            _selections[Path.GetFullPath(folder)] = values.Select(value => value with { Revision = revision }).ToArray();
        }
        Notify(folder);
    }

    private void Notify(string folder)
    {
        Action<string>[] listeners;
        lock (_gate) listeners = _listeners.Values.ToArray();
        foreach (var listener in listeners)
        {
            try { listener(folder); }
            catch (Exception error)
            {
                // A faulty observer cannot invalidate a committed selection or leak a registration lease.
                System.Diagnostics.Trace.TraceError("Character model observer failed: {0}", error);
            }
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
