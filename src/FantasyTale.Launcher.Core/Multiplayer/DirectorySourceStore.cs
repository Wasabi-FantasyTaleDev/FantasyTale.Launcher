using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Local overrides layered over store-supplied and bundled server-discovery sources.</summary>
public sealed class DirectorySourceStore
{
    private static readonly DirectorySource Default = ReadDefault();
    private readonly string _root;

    /// <summary>Opens a source directory without contacting any remote directory.</summary>
    public DirectorySourceStore(string root)
    { _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root); }

    /// <summary>Combines the offline fallback, store defaults and user overrides without rewriting local settings.</summary>
    public IReadOnlyList<DirectorySource> List(IReadOnlyList<DirectorySource>? catalogSources = null)
    {
        var result = new Dictionary<string, DirectorySource>(StringComparer.Ordinal) { [Default.Id] = Default };
        foreach (var source in catalogSources ?? [])
        {
            Validate(source);
            result[source.Id] = source with { BuiltIn = true };
        }
        var files = Directory.EnumerateFiles(_root, "*.toml");
        foreach (var file in files)
        {
            InstanceStore.RejectLinks(file); var source = Read(TomlReader.Load(file));
            if (Path.GetFileNameWithoutExtension(file) != source.Id) throw new InvalidDataException("Directory source identity does not match its file name.");
            // Older installations saved the bundled slot before the official endpoint was available.
            result.TryGetValue(source.Id, out var supplied);
            if (source.Id == Default.Id && source.BaseUrl is { } address && Uri.TryCreate(address, UriKind.Absolute, out var previous) &&
                previous.Host.Equals("bf.wanwanlocksmith.pp.ua", StringComparison.OrdinalIgnoreCase))
                source = source with { BaseUrl = Default.BaseUrl };
            result[source.Id] = source with { BuiltIn = supplied is not null, BaseUrl = source.BaseUrl ?? supplied?.BaseUrl };
        }
        return result.Values.OrderByDescending(s => s.BuiltIn).ThenBy(s => s.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Atomically saves a source without storing administrator or room tokens.</summary>
    public void Save(DirectorySource source)
    {
        Validate(source);
        var file = Path.Combine(_root, source.Id + ".toml"); InstanceStore.RejectLinks(file);
        var lockPath = Path.Combine(_root, ".lock"); InstanceStore.RejectLinks(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var text = $"schema = 1\n[source]\nid = {SessionWriter.Quote(source.Id)}\nname = {SessionWriter.Quote(source.Name)}\nbase_url = {SessionWriter.Quote(source.BaseUrl ?? "")}\nenabled = {(source.Enabled ? "true" : "false")}\n";
        var temporary = file + ".saving"; InstanceStore.RejectLinks(temporary);
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); AtomicFile.Replace(temporary, file); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Removes a custom source while preserving bundled and currently enabled store source slots.</summary>
    public void Delete(string id, IReadOnlyList<DirectorySource>? catalogSources = null)
    {
        DirectoryValidation.Id(id);
        if (id == Default.Id || catalogSources?.Any(source => source.Id == id) == true)
            throw new InvalidOperationException("Disable or edit the supplied directory source instead.");
        var file = Path.Combine(_root, id + ".toml"); InstanceStore.RejectLinks(file); File.Delete(file);
    }

    private static DirectorySource ReadDefault()
    {
        using var stream = typeof(DirectorySourceStore).Assembly.GetManifestResourceStream("FantasyTale.Launcher.Core.Multiplayer.default-directory.toml")!;
        using var reader = new StreamReader(stream);
        return Read(TomlReader.Parse("default-directory.toml", reader.ReadToEnd()), bundled: true);
    }
    private static DirectorySource Read(TomlReader root, bool bundled = false)
    {
        root.OnlyKeys("schema", "source"); if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var s = root.Table("source", true)!; s.OnlyKeys("id", "name", "base_url", "enabled");
        var id = s.String("id", true);
        var result = new DirectorySource(id, s.String("name", true), s.String("base_url") is { Length: > 0 } url ? url : null, s.Boolean("enabled", true), bundled || id == Default.Id);
        if (!bundled) Validate(result); return result;
    }
    private static void Validate(DirectorySource source)
    {
        DirectoryValidation.Id(source.Id); DirectoryValidation.Text(source.Name, 128);
        if (source.BaseUrl is null && source.Id != Default.Id) throw new ArgumentException("A directory API address is required.");
        if (source.BaseUrl is not null) _ = ServerStore.ApiBase(source.BaseUrl);
    }
}
