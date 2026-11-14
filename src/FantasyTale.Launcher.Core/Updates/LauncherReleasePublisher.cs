using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Scans a launcher build and publishes the same generated file index for HTTP and AList.</summary>
public static class LauncherReleasePublisher
{
    /// <summary>Copies content objects and atomically publishes their generated index without a manually maintained file list.</summary>
    /// <returns>The path of the published current index.</returns>
    public static async Task<string> PublishAsync(string configuration, CancellationToken ct = default)
    {
        var config = TomlReader.Load(configuration);
        config.OnlyKeys("input", "output", "feed_path", "source_prefix", "platform", "revision", "version", "signature_path", "signature_private_key_env", "signature_public_key_path", "deprecated_before_version");
        var configRoot = Path.GetDirectoryName(Path.GetFullPath(configuration))!;
        var input = Path.GetFullPath(config.String("input", true), configRoot);
        var output = Path.GetFullPath(config.String("output", true), configRoot);
        foreach (var path in new[] { input, output }) InstanceStore.RejectLinks(path);
        if (Path.TrimEndingDirectorySeparator(input).Equals(Path.TrimEndingDirectorySeparator(output), StringComparison.OrdinalIgnoreCase) || InstanceStore.IsWithin(input, output) || InstanceStore.IsWithin(output, input))
            throw new InvalidDataException("Build and publication directories must be separate.");
        var platform = config.String("platform", fallback: LauncherUpdatePolicy.CurrentPlatform);
        if (platform is not ("win-x64" or "win-arm64" or "win-x86")) throw new InvalidDataException("Unsupported publication platform.");
        var revision = config.Integer("revision", min: 1) ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var version = config.String("version");
        if (version.Length > 64 || version.Any(char.IsControl)) throw new InvalidDataException("Invalid publication version label.");
        var deprecated = config.String("deprecated_before_version");
        if (deprecated.Length > 0 && (PluginVersion.Parse(deprecated).Pre.Length > 0 || PluginCatalog.CompareVersions(deprecated, version) > 0))
            throw new InvalidDataException("deprecated_before_version must be a release version no later than the published release.");
        var feed = SourceAddress.RelativePath(config.String("feed_path", fallback: $"launcher/{platform}/current.toml"));
        var prefix = SourceAddress.RelativePath(config.String("source_prefix", fallback: $"launcher/{platform}"));
        if (feed.StartsWith(prefix + "/objects/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The feed cannot overwrite a content object.");
        var signaturePath = config.String("signature_path");
        var signingEnvironment = config.String("signature_private_key_env");
        var publicKeyPath = config.String("signature_public_key_path");
        if (new[] { signaturePath, signingEnvironment, publicKeyPath }.Count(value => value.Length > 0) is not (0 or 3))
            throw new InvalidDataException("signature_path, signature_private_key_env and signature_public_key_path must be supplied together.");
        if (signaturePath.Length > 0) { signaturePath = SourceAddress.RelativePath(signaturePath); publicKeyPath = SourceAddress.RelativePath(publicKeyPath); }
        ValidatePublicationPaths(feed, prefix, signaturePath, publicKeyPath);
        var signatureRecord = UpdateFiles.Safe(output, ".fantasytale-signature.json");
        if (signaturePath.Length == 0 && File.Exists(signatureRecord))
            throw new InvalidDataException("A signed publication output cannot be reused for an unsigned release.");
        var text = new StringBuilder($"schema = 1\nproduct = \"fantasytale-launcher\"\nplatform = {Quote(platform)}\nrevision = {revision}\nversion = {Quote(version)}\n");
        // Launchers before 0.2.0 only understand the inclusive deprecated_max_version. The lowest pre-release of the
        // boundary ("0.2.0-0") is above every earlier release and below the boundary itself, so they are held to exactly
        // the same rule.
        if (deprecated.Length > 0) text.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"deprecated_before_version = {Quote(deprecated)}\ndeprecated_max_version = {Quote(deprecated + "-0")}\n");
        var rows = Enumerate(input); var destinations = rows.Select(p => Path.GetRelativePath(input, p).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (destinations.Contains("launcher-release-policy.toml"))
            throw new InvalidDataException("The retired release policy file cannot be packaged; configure deprecated_before_version in the release index.");
        if (!destinations.Contains(LauncherRelease.Executable) || !destinations.Contains("wwwroot/index.html") || !destinations.Contains(LauncherRelease.UpdaterExecutable))
            throw new InvalidDataException("Publish a complete launcher build including its frontend and updater.");
        // Manifests are tracked but native payloads are staged separately; validate even optional plugins before exposing a feed.
        foreach (var pluginManifest in rows.Where(path => Path.GetFileName(path).Equals("plugin.toml", StringComparison.OrdinalIgnoreCase)
            && Path.GetRelativePath(input, path).Replace('\\', '/').StartsWith("games/", StringComparison.OrdinalIgnoreCase)))
        {
            var plugin = PluginCatalog.LoadFile(pluginManifest);
            if (plugin.Library is not { } library) continue;
            var relative = Path.GetRelativePath(input, Path.Combine(plugin.Folder, ClientRelease.ValidatePath(library))).Replace('\\', '/');
            if (!destinations.Contains(relative) || new FileInfo(Path.Combine(input, relative)).Length == 0)
                throw new DefinitionException(pluginManifest, "native.library", "bundled native library is missing or empty; stage native plugins before publication");
        }
        Directory.CreateDirectory(output);
        using var lease = new FileStream(UpdateFiles.Safe(output, ".fantasytale-publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var objects = UpdateFiles.Safe(output, prefix + "/objects"); Directory.CreateDirectory(objects);
        var statePath = UpdateFiles.Safe(output, ".fantasytale-publish.json");
        var previous = UpdateFiles.Read<Published>(statePath);
        if (previous is not null && (previous.Platform != platform || previous.Revision >= revision))
            throw new InvalidDataException("Publication revision must advance without changing the output's platform.");
        long total = 0;
        foreach (var path in rows)
        {
            ct.ThrowIfCancellationRequested(); var relative = ClientRelease.ValidatePath(Path.GetRelativePath(input, path).Replace('\\', '/'));
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
            total = checked(total + source.Length);
            var size = source.Length; var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, ct).ConfigureAwait(false)).ToLowerInvariant(); source.Position = 0;
            var destination = UpdateFiles.Safe(objects, hash);
            if (!File.Exists(destination))
            {
                var temp = UpdateFiles.Safe(objects, "publishing.tmp");
                await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { await source.CopyToAsync(target, ct).ConfigureAwait(false); target.Flush(true); }
                if (!await Downloads.HttpDownloadEngine.MatchesAsync(temp, size, hash, ct).ConfigureAwait(false)) throw new IOException("Build file changed during publication.");
                File.Move(temp, destination, false);
            }
            else if (!await Downloads.HttpDownloadEngine.MatchesAsync(destination, size, hash, ct).ConfigureAwait(false)) throw new InvalidDataException("An existing published object was modified.");
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n[[files]]\npath = {Quote(relative)}\nsource_path = {Quote(prefix + "/objects/" + hash)}\nsize = {size}\nsha256 = \"{hash}\"\n");
        }
        var manifest = Encoding.UTF8.GetBytes(text.ToString());
        var feedPath = UpdateFiles.Safe(output, feed); Directory.CreateDirectory(Path.GetDirectoryName(feedPath)!);
        var temporary = UpdateFiles.Safe(output, feed + ".tmp");
        using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { target.Write(manifest); target.Flush(true); }
        if (signaturePath.Length > 0)
        {
            var encoded = Environment.GetEnvironmentVariable(signingEnvironment);
            if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidDataException("Launcher signing key environment variable is empty.");
            byte[] signature, publicKey;
            try
            {
                using var rsa = RSA.Create(); rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(encoded), out _);
                signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                publicKey = rsa.ExportSubjectPublicKeyInfo();
            }
            catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException)
            { throw new InvalidDataException("Launcher signing key is invalid.", error); }
            var signatureFile = UpdateFiles.Safe(output, signaturePath); Directory.CreateDirectory(Path.GetDirectoryName(signatureFile)!);
            var signatureTemporary = signatureFile + ".tmp";
            await File.WriteAllBytesAsync(signatureTemporary, signature, ct).ConfigureAwait(false); File.Move(signatureTemporary, signatureFile, true);
            var publicFile = UpdateFiles.Safe(output, publicKeyPath); Directory.CreateDirectory(Path.GetDirectoryName(publicFile)!);
            var publicTemporary = publicFile + ".tmp";
            var publicBytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(publicKey) + "\n");
            await File.WriteAllBytesAsync(publicTemporary, publicBytes, ct).ConfigureAwait(false); File.Move(publicTemporary, publicFile, true);
            UpdateFiles.Write(signatureRecord, new
            {
                feed_path = feed, sha256 = Convert.ToHexStringLower(SHA256.HashData(manifest)),
                signature_path = signaturePath, signature_sha256 = Convert.ToHexStringLower(SHA256.HashData(signature)),
                public_key_path = publicKeyPath, public_key_sha256 = Convert.ToHexStringLower(SHA256.HashData(publicBytes)),
            });
        }
        // Reserve the revision before publishing so a power loss cannot cause the same number to be reused.
        UpdateFiles.Write(statePath, new Published(platform, revision));
        File.Move(temporary, feedPath, true);
        return feedPath;
    }
    private sealed record Published(string Platform, long Revision);
    private static void ValidatePublicationPaths(string feed, string prefix, string signature, string publicKey)
    {
        var paths = new List<string> { feed, feed + ".tmp", ".fantasytale-publish.lock", ".fantasytale-publish.json", ".fantasytale-publish.json.tmp", ".fantasytale-signature.json", ".fantasytale-signature.json.tmp" };
        if (signature.Length > 0) paths.AddRange([signature, signature + ".tmp", publicKey, publicKey + ".tmp"]);
        foreach (var path in paths)
            if (path.Equals(prefix + "/objects", StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/objects/", StringComparison.OrdinalIgnoreCase)
                || (prefix + "/objects").StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Publication metadata cannot overwrite content objects.");
        for (var index = 0; index < paths.Count; index++)
            for (var other = index + 1; other < paths.Count; other++)
                if (paths[index].Equals(paths[other], StringComparison.OrdinalIgnoreCase)
                    || paths[index].StartsWith(paths[other] + "/", StringComparison.OrdinalIgnoreCase)
                    || paths[other].StartsWith(paths[index] + "/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Publication metadata paths must be independent, including temporary files.");
    }
    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static string[] Enumerate(string input)
    {
        var result = new List<string>(); var pending = new Stack<string>(); pending.Push(input);
        while (pending.TryPop(out var folder))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                InstanceStore.RejectLinks(path); var name = Path.GetFileName(path);
                if (name.Equals(LauncherUpdatePolicy.FileName, StringComparison.OrdinalIgnoreCase) || name.StartsWith(".fantasytale", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || name.Equals(".env", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".local.toml", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("remembered-logins.bin", StringComparison.OrdinalIgnoreCase) || name.StartsWith("account-sessions.bin", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Remove local secrets and overrides from the release input.");
                if (Directory.Exists(path)) pending.Push(path);
                else result.Add(path);
            }
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
