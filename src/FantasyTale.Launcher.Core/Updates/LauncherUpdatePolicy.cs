using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Downloads.AList;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Launcher update source settings, kept separate from game and plugin catalogs.</summary>
public sealed class LauncherUpdatePolicy
{
    /// <summary>Name of the optional source configuration beside the launcher.</summary>
    public const string FileName = "launcher-update.toml";
    /// <summary>Shared source adapter: HTTP, AList or GitHub.</summary>
    public string Kind { get; }
    /// <summary>User-facing source label, independent of its transport.</summary>
    public string Name { get; private init; } = "";
    /// <summary>Whether plain HTTP source requests use resolved IP authorities.</summary>
    public bool PreferIp { get; private init; }
    /// <summary>Public source root without credentials or temporary download parameters.</summary>
    public Uri BaseUrl { get; }
    /// <summary>Path of the generated current file index within the source.</summary>
    public string FeedPath { get; }
    /// <summary>Runtime identifier of the current launcher process.</summary>
    public string Platform { get; }
    /// <summary>Baseline revision of an initial installation when no update receipt exists.</summary>
    public long MinimumRevision { get; }
    /// <summary>Optional detached signature path for the exact feed bytes.</summary>
    public string? SignaturePath { get; }
    /// <summary>Optional base64 SubjectPublicKeyInfo used to verify the detached feed signature.</summary>
    public string? SignaturePublicKey { get; }
    private string? _retiredIdentityUrl;
    internal string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Kind + "\n" + (_retiredIdentityUrl ?? BaseUrl.AbsoluteUri) + "\n" + FeedPath)));
    internal static string CurrentPlatform => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    private LauncherUpdatePolicy(string kind, Uri baseUrl, string feedPath, long minimumRevision, string? signaturePath, string? signaturePublicKey)
    { Kind = kind; BaseUrl = baseUrl; FeedPath = feedPath; Platform = CurrentPlatform; MinimumRevision = minimumRevision; SignaturePath = signaturePath; SignaturePublicKey = signaturePublicKey; }

    /// <summary>Reads an update source; only its kind and base URL are required.</summary>
    public static LauncherUpdatePolicy? Load(string installationDirectory)
    {
        var sources = LoadAll(installationDirectory);
        return sources.Count == 0 ? null : sources[0];
    }

    /// <summary>Reads the ordered launcher update sources, retaining schema-one compatibility.</summary>
    public static IReadOnlyList<LauncherUpdatePolicy> LoadAll(string installationDirectory)
    {
        var path = ClientInstallTransaction.Safe(installationDirectory, FileName);
        if (!File.Exists(path)) return [];
        var root = TomlReader.Load(path);
        var schema = root.Integer("schema") ?? 1;
        if (schema == 1)
        {
            root.OnlyKeys("schema", "name", "kind", "base_url", "feed_path", "minimum_revision", "signature_path", "signature_public_key", "prefer_ip");
            return [Read(root)];
        }
        if (schema != 2) throw root.Error("schema", "unsupported update settings");
        root.OnlyKeys("schema", "sources");
        var sources = root.Tables("sources").Select(Read).ToArray();
        if (sources.Length == 0) throw root.Error("sources", "at least one source is required");
        if (sources.Select(source => source.Identity).Distinct(StringComparer.Ordinal).Count() != sources.Length)
            throw root.Error("sources", "duplicate source endpoints are not allowed");
        return sources;
    }

    private sealed record Selection(string Identity);

    internal static LauncherUpdatePolicy? LoadSelected(string installationDirectory, string updateRoot)
    {
        var sources = LoadAll(installationDirectory);
        var selection = UpdateFiles.Read<Selection>(UpdateFiles.Safe(updateRoot, "source.json"));
        return selection is null ? sources.Count == 0 ? null : sources[0]
            : sources.SingleOrDefault(source => source.Identity == selection.Identity);
    }

    internal void SaveSelection(string updateRoot) => UpdateFiles.Write(UpdateFiles.Safe(updateRoot, "source.json"), new Selection(Identity));

    private static LauncherUpdatePolicy Read(TomlReader root)
    {
        root.OnlyKeys("schema", "name", "kind", "base_url", "feed_path", "minimum_revision", "signature_path", "signature_public_key", "prefer_ip");
        var kind = root.String("kind", true);
        if (kind is not ("http" or "alist" or "github")) throw root.Error("kind", "must be http, alist or github");
        var signaturePath = root.String("signature_path");
        var publicKey = root.String("signature_public_key");
        if (signaturePath.Length == 0 != (publicKey.Length == 0)) throw root.Error("signature_path", "signature_path and signature_public_key must be supplied together");
        var url = SourceAddress.BaseUrl(root.String("base_url", true));
        SourceTransport.Validate(kind, url.AbsoluteUri);
        var name = root.String("name", fallback: url.Host);
        var feedPath = SourceAddress.RelativePath(root.String("feed_path", fallback: $"launcher/{CurrentPlatform}/current.toml"));
        var preferIp = root.Boolean("prefer_ip", fallback: false);
        string? retiredIdentityUrl = null;
        if (kind == "alist" && url.Scheme == "http" && url.Port == 5244 && url.AbsolutePath == "/" &&
            url.Host.Equals("wanwanlocksmith.pp.ua", StringComparison.OrdinalIgnoreCase) &&
            feedPath == $"FantasyTale/launcher/{CurrentPlatform}/current.toml" && signaturePath.Length == 0)
        {
            // The updater preserves local policies, so retired bundled endpoints must migrate when read.
            // Retain their source identity until explicitly saved so staged transfers and accepted revisions survive.
            retiredIdentityUrl = url.AbsoluteUri;
            using var bundled = typeof(StoreSourceStore).Assembly.GetManifestResourceStream("FantasyTale.Launcher.Core.Stores.default-source.toml")!;
            using var reader = new StreamReader(bundled);
            var source = TomlReader.Parse("default-source.toml", reader.ReadToEnd()).Table("source", true)!;
            url = SourceAddress.BaseUrl(source.String("base_url", true));
            preferIp = source.Boolean("prefer_ip", fallback: false);
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw root.Error("name", "requires 1..128 printable characters");
        return new(kind, url, feedPath,
            root.Integer("minimum_revision", min: 0) ?? 0, signaturePath.Length == 0 ? null : SourceAddress.RelativePath(signaturePath), publicKey.Length == 0 ? null : publicKey)
        { Name = name, PreferIp = preferIp, _retiredIdentityUrl = retiredIdentityUrl };
    }

    internal static void SaveAll(string installation, IReadOnlyList<LauncherUpdateSourceSettings> sources)
    {
        if (sources.Count < 1) throw new ArgumentException("Configure at least one launcher update source.");
        var text = new StringBuilder("schema = 2\n");
        foreach (var source in sources)
        {
            if (source is null) throw new ArgumentException("Update source cannot be empty.");
            if (source.Name is null || source.Kind is null || source.BaseUrl is null || source.FeedPath is null || source.MinimumRevision < 0)
                throw new ArgumentException("Update source fields and a nonnegative baseline are required.");
            text.Append("\n[[sources]]\n");
            void Line(string key, string value) => text.Append(key).Append(" = ").Append(Hooks.SessionWriter.Quote(value)).Append('\n');
            Line("name", source.Name); Line("kind", source.Kind); Line("base_url", source.BaseUrl); Line("feed_path", source.FeedPath);
            text.Append("minimum_revision = ").Append(source.MinimumRevision).Append('\n');
            text.Append("prefer_ip = ").Append(source.PreferIp ? "true\n" : "false\n");
            if (!string.IsNullOrEmpty(source.SignaturePath)) Line("signature_path", source.SignaturePath);
            if (!string.IsNullOrEmpty(source.SignaturePublicKey)) Line("signature_public_key", source.SignaturePublicKey);
        }
        var document = text.ToString();
        var parsed = TomlReader.Parse(FileName, document).Tables("sources").Select(Read).ToArray();
        foreach (var policy in parsed) policy.ValidateRuntime();
        if (parsed.Select(policy => policy.Identity).Distinct(StringComparer.Ordinal).Count() != parsed.Length)
            throw new ArgumentException("Duplicate launcher update sources are not allowed.");
        var path = ClientInstallTransaction.Safe(installation, FileName);
        var temp = ClientInstallTransaction.Safe(installation, ".launcher-update-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(Encoding.UTF8.GetBytes(document)); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Creates the same source adapter used by client and plugin downloads.</summary>
    public IDownloadSource OpenSource(HttpClient http) => SourceTransport.Open(http, Kind, BaseUrl, PreferIp);
    internal void ValidateRuntime()
    {
        if (!OperatingSystem.IsWindows() || Platform is not ("win-x64" or "win-x86" or "win-arm64"))
            throw new InvalidDataException("Launcher updates require a supported Windows runtime.");
        if (SignaturePublicKey is not null)
        {
            try
            {
                var key = Convert.FromBase64String(SignaturePublicKey);
                using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(key, out _);
            }
            catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException)
            { throw new InvalidDataException("Launcher update signing key is invalid.", error); }
        }
    }

    internal void VerifySignature(byte[] manifest, byte[] signature)
    {
        if (SignaturePublicKey is null) return;
        try
        {
            using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(SignaturePublicKey), out _);
            if (!rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new CryptographicException("Launcher update signature verification failed.");
        }
        catch (FormatException error) { throw new InvalidDataException("Launcher update signature is not valid base64.", error); }
    }

    internal LauncherRelease ReadStagedRelease(string updateRoot)
    {
        ValidateRuntime();
        var index = UpdateFiles.ReadIndex(UpdateFiles.Safe(updateRoot, "release.toml"));
        if (SignaturePath is not null)
        {
            var signature = UpdateFiles.Safe(updateRoot, "release.toml.sig");
            if (!File.Exists(signature)) throw new InvalidDataException("The saved update signature is missing. Check for updates again.");
            VerifySignature(index, UpdateFiles.ReadIndex(signature));
        }
        return LauncherRelease.Parse(this, index);
    }
}
