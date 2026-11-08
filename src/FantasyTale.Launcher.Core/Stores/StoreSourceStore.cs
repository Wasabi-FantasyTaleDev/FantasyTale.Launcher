using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Persists store source definitions, without storing credentials or self-update authority.</summary>
public sealed class StoreSourceStore
{
    private readonly string _root;
    private static readonly StoreSource Default = ReadDefault();

    /// <summary>Opens the local store-source directory and rejects reparse points.</summary>
    public StoreSourceStore(string root)
    {
        Validate(Default);
        _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root);
    }

    /// <summary>Returns the bundled source slot with local overrides and user sources.</summary>
    public IReadOnlyList<StoreSource> List()
    {
        var sources = new Dictionary<string, StoreSource>(StringComparer.Ordinal) { [Default.Id] = Default };
        var files = Directory.EnumerateFiles(_root, "*.toml");
        foreach (var file in files)
        {
            InstanceStore.RejectLinks(file); var root = TomlReader.Load(file);
            var source = Read(root); var id = source.Id;
            if (id == Default.Id && source.BaseUrl is { } configured &&
                Uri.TryCreate(configured, UriKind.Absolute, out var oldAddress) && oldAddress.Host.Equals("wanwanlocksmith.pp.ua", StringComparison.OrdinalIgnoreCase))
                source = source with { BaseUrl = Default.BaseUrl, PreferIp = true };
            if (id == Default.Id && source.Name == "Fantasy Tale" && source.BaseUrl == Default.BaseUrl)
                source = source with { Name = Default.Name };
            Validate(source);
            if (Path.GetFileNameWithoutExtension(file) != id) throw root.Error("source.id", "must match filename");
            // Older installations persisted an empty bundled slot before an endpoint was provided.
            // Explicit addresses and the user's enabled/disabled choice continue to take precedence.
            sources[id] = id == Default.Id && source.BaseUrl is null && source.Kind == Default.Kind
                ? source with { BaseUrl = Default.BaseUrl } : source;
        }
        return sources.Values.OrderByDescending(s => s.BuiltIn).ThenBy(s => s.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Atomically saves a source; an address is optional only for the bundled slot.</summary>
    public void Save(StoreSource source)
    {
        Validate(source);
        using var lease = Lease();
        var file = Path.Combine(_root, source.Id + ".toml"); InstanceStore.RejectLinks(file);
        var text = new StringBuilder("schema = 1\n[source]\n");
        void Line(string key, string value) => text.Append(key).Append(" = ").Append(SessionWriter.Quote(value)).Append('\n');
        Line("id", source.Id); Line("name", source.Name); Line("kind", source.Kind); Line("base_url", source.BaseUrl is null ? "" : SourceAddress.BaseUrl(source.BaseUrl).AbsoluteUri);
        Line("catalog_path", source.CatalogPath); text.Append("enabled = ").Append(source.Enabled ? "true\n" : "false\n");
        if (source.PreferIp is { } preferIp) text.Append("prefer_ip = ").Append(preferIp ? "true\n" : "false\n");
        var temporary = Path.Combine(_root, ".saving-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try { File.Move(temporary, file, true); break; }
                // A source reader or Windows scanner may retain the replaced snapshot briefly.
                catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException
                    && (error.HResult & 0xffff) is 5 or 32 or 33 && elapsed.Elapsed < TimeSpan.FromSeconds(1))
                { Thread.Sleep(10); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Removes a user source definition while preserving every installed client and plugin.</summary>
    public void Delete(string id)
    {
        InstanceFile.ValidateId("source.toml", id);
        if (id == Default.Id) throw new InvalidOperationException("The bundled source can be disabled, not removed.");
        using var lease = Lease(); var file = Path.Combine(_root, id + ".toml"); InstanceStore.RejectLinks(file); File.Delete(file);
    }

    private FileStream Lease()
    {
        var path = Path.Combine(_root, ".lock"); InstanceStore.RejectLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static StoreSource ReadDefault()
    {
        using var stream = typeof(StoreSourceStore).Assembly.GetManifestResourceStream("FantasyTale.Launcher.Core.Stores.default-source.toml")
            ?? throw new InvalidOperationException("Missing bundled store source definition.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return Read(TomlReader.Parse("default-source.toml", reader.ReadToEnd()), builtIn: true);
    }

    private static StoreSource Read(TomlReader root, bool builtIn = false)
    {
        root.OnlyKeys("schema", "source");
        if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var s = root.Table("source", true)!; s.OnlyKeys("id", "name", "kind", "base_url", "catalog_path", "enabled", "prefer_ip");
        var id = s.String("id", true);
        return new StoreSource(id, s.String("name", true), s.String("kind", true), s.String("base_url") is { Length: > 0 } url ? url : null,
            s.String("catalog_path", true), s.Boolean("enabled", true), builtIn || id == Default.Id)
        { PreferIp = s.Has("prefer_ip") ? s.Boolean("prefer_ip") : null };
    }

    private static void Validate(StoreSource source)
    {
        InstanceFile.ValidateId("source.toml", source.Id);
        if (string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 128 || source.Name.Any(char.IsControl)) throw new ArgumentException("Source name requires 1..128 printable characters.");
        if (source.Kind is not ("http" or "alist" or "github")) throw new ArgumentException("Unknown source transport.");
        if (source.BuiltIn != (source.Id == Default.Id)) throw new ArgumentException("Invalid bundled source identity.");
        if (source.BaseUrl is null && !source.BuiltIn) throw new ArgumentException("A source URL is required.");
        if (source.BaseUrl is not null) SourceTransport.Validate(source.Kind, source.BaseUrl);
        _ = SourceAddress.RelativePath(source.CatalogPath);
    }
}
