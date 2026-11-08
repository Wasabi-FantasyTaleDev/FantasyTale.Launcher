using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Materializes store subscriptions into immutable, hash-checked GitHub server archives.</summary>
public static class GitHubStoreReleases
{
    /// <summary>Rejects ambiguous repositories, release tags, archive names and digests before constructing any transport URL.</summary>
    public static void Validate(StoreGitHubAsset asset)
    {
        var repository = asset.Repository.Split('/');
        if (repository.Length != 2 || repository.Any(part => part is "" or "." or ".." || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))))
            throw new ArgumentException("GitHub repositories must use owner/repository without URL suffixes.");
        if (string.IsNullOrWhiteSpace(asset.Tag) || asset.Tag != asset.Tag.Trim() || asset.Tag.Any(char.IsControl) || asset.Tag is "." or "..")
            throw new ArgumentException("A nonempty published release tag is required.");
        _ = SourceAddress.RelativePath(asset.Asset);
        if (asset.Asset.Contains('/') || !asset.Asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A GitHub server asset must be one ZIP filename.");
        if (asset.Sha256.Length != 64 || !asset.Sha256.All(char.IsAsciiHexDigit)) throw new ArgumentException("A GitHub asset requires its SHA-256 digest.");
        _ = ClientRelease.ValidatePath(asset.Executable);
        if (!asset.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A server archive requires its executable filename.");
    }

    /// <summary>Creates a public transport rooted at the selected tag, never the mutable latest-release redirect.</summary>
    public static IDownloadSource Open(StoreGitHubAsset asset)
    {
        Validate(asset);
        return new HttpSource(new Uri($"https://github.com/{asset.Repository}/releases/download/{Uri.EscapeDataString(asset.Tag)}/"));
    }

    /// <summary>Builds the deterministic archive manifest retained by queued, paused and restored download tasks.</summary>
    public static byte[] Manifest(StorePackage package)
    {
        var asset = package.GitHubAsset ?? throw new InvalidDataException("The package has no pinned GitHub asset.");
        Validate(asset);
        if (package.Kind != "server") throw new InvalidDataException("GitHub release subscriptions require server packages.");
        var quote = SessionWriter.Quote;
        var text = new StringBuilder("schema = 1\n[github_asset]\nrepository = ");
        text.Append(quote(asset.Repository)).Append("\ntag = ").Append(quote(asset.Tag)).Append("\n[release]\n");
        text.Append("id = ").Append(quote(package.Id)).Append("\ngame = ").Append(quote(package.Game))
            .Append("\nbuilds = [").Append(string.Join(", ", package.Builds.Select(quote))).Append("]\nversion = ").Append(quote(package.Version))
            .Append("\nformat = \"archive\"\narchive_format = \"zip\"\n");
        text.Append("executable = ").Append(quote(asset.Executable)).Append('\n');
        text.Append("[[files]]\npath = ").Append(quote(asset.Asset)).Append("\nsource_path = ").Append(quote(asset.Asset))
            .Append("\nsize = ").Append(package.Size.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("\nsha256 = ").Append(quote(asset.Sha256.ToLowerInvariant())).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    internal static async Task<(string Text, bool Offline)> ResolveAsync(HttpClient http, string text, StoreCatalog catalog, StoreCatalog? previous,
        GitHubRateLimit rateLimit, CancellationToken ct)
    {
        var responses = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement>(StringComparer.Ordinal);
        var offline = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(catalog.GitHubReleases.Select(item => item.Repository).Distinct(StringComparer.Ordinal),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (repository, token) =>
            {
                if (rateLimit.Active) { offline[repository] = true; return; }
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
                request.Headers.UserAgent.ParseAdd("FantasyTale-Launcher");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                    rateLimit.Observe(response);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                    using var json = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false);
                    responses[repository] = json.RootElement.Clone();
                }
                catch (HttpRequestException) { offline[repository] = true; }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { offline[repository] = true; }
            }).ConfigureAwait(false);
        var document = TomlSerializer.Deserialize<TomlTable>(text) ?? throw new InvalidDataException("Empty store catalog.");
        var packages = new TomlTableArray();
        if (document.TryGetValue("packages", out var existing))
            foreach (var row in (TomlTableArray)existing)
                if (!catalog.GitHubReleases.Any(item => item.Id == (string)row["id"])) packages.Add(row);
        foreach (var subscription in catalog.GitHubReleases)
        {
            StorePackage package;
            if (offline.ContainsKey(subscription.Repository))
            {
                var fallback = (previous?.Packages ?? []).Concat(catalog.Packages).FirstOrDefault(item => item.Id == subscription.Id &&
                    item.Game == subscription.Game && item.Builds.SequenceEqual(subscription.Builds) && item.GitHubAsset is { } pin &&
                    pin.Repository == subscription.Repository && pin.Asset == subscription.Asset && pin.Executable == subscription.Executable);
                if (fallback is null) continue;
                package = fallback with { Name = subscription.Name, Description = subscription.Description, Publisher = subscription.Publisher };
            }
            else
            {
                var release = responses[subscription.Repository];
                try
                {
                    if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                        throw new InvalidDataException("The subscribed GitHub release is not a published stable release.");
                    var tag = release.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The GitHub release has no tag.");
                    var version = tag.StartsWith('v') ? tag[1..] : tag;
                    PluginCatalog.ValidateVersion(subscription.Repository, "tag_name", version);
                    var matches = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == subscription.Asset).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("The subscribed GitHub archive is absent or ambiguous.");
                    var selected = matches[0];
                    var digest = selected.GetProperty("digest").GetString();
                    if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal))
                        throw new InvalidDataException("The GitHub archive has no SHA-256 digest.");
                    var asset = new StoreGitHubAsset(subscription.Repository, tag, subscription.Asset, digest[7..].ToLowerInvariant(), subscription.Executable);
                    Validate(asset);
                    var expectedUrl = (await Open(asset).ResolveAsync(asset.Asset, ct).ConfigureAwait(false)).Url;
                    if (!Uri.TryCreate(selected.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var actualUrl) || actualUrl != expectedUrl)
                        throw new InvalidDataException("GitHub asset URL disagrees with the selected repository, tag or archive.");
                    var size = selected.GetProperty("size").GetInt64();
                    if (size <= 0) throw new InvalidDataException("The GitHub archive is empty.");
                    package = new StorePackage(subscription.Id, "server", subscription.Game, subscription.Builds, version,
                        subscription.Name, subscription.Description, subscription.Publisher, size, "github-release.toml", "",
                        release.GetProperty("published_at").GetDateTimeOffset().ToUnixTimeSeconds()) { GitHubAsset = asset };
                    package = package with { ManifestSha256 = Convert.ToHexStringLower(SHA256.HashData(Manifest(package))) };
                    _ = ClientRelease.Parse(package, Manifest(package));
                }
                catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
                { throw new InvalidDataException("Invalid GitHub release metadata for " + subscription.Id + ".", error); }
            }
            var pinned = package.GitHubAsset!;
            var builds = new TomlArray(); foreach (var build in package.Builds) builds.Add(build);
            var github = new TomlTable { ["repository"] = pinned.Repository, ["tag"] = pinned.Tag, ["asset"] = pinned.Asset, ["sha256"] = pinned.Sha256 };
            github["executable"] = pinned.Executable;
            var row = new TomlTable
            {
                ["id"] = package.Id, ["kind"] = package.Kind, ["game"] = package.Game, ["builds"] = builds, ["version"] = package.Version,
                ["name"] = Localized(package.Name), ["publisher"] = package.Publisher,
                ["size"] = package.Size, ["manifest"] = package.Manifest, ["manifest_sha256"] = package.ManifestSha256, ["revision"] = package.Revision,
                ["github_asset"] = github,
            };
            if (package.Description.Count > 0) row["description"] = Localized(package.Description);
            packages.Add(row);
        }
        // Keep each root table array contiguous when profiles and directory rows follow packages.
        var materialized = new TomlTable { ["schema"] = document["schema"], ["catalog"] = document["catalog"], ["packages"] = packages };
        foreach (var pair in document.Where(pair => pair.Key is not ("schema" or "catalog" or "packages"))) materialized.Add(pair.Key, pair.Value);
        return (TomlSerializer.Serialize(materialized), !offline.IsEmpty);
    }

    private static TomlTable Localized(IReadOnlyDictionary<string, string> values)
    {
        var table = new TomlTable();
        foreach (var value in values) table.Add(value.Key, value.Value);
        return table;
    }
}
