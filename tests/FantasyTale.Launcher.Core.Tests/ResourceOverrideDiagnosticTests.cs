using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceOverrideDiagnosticTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-resource-diagnostics-").FullName;

    [Fact]
    public async Task KeepsContributorsTogetherAndObservesDeploymentWithoutMutatingFiles()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAtAsync(new("test", "Test", "sample", "sample", null, []), null);
        var client = store.ClientFolder("test");
        Directory.CreateDirectory(Path.Combine(client, "Packs"));
        const string target = "Packs/Models.pkn";
        var original = "original archive"u8.ToArray();
        var replacement = "composed archive"u8.ToArray();
        var path = Path.Combine(client, target);
        File.WriteAllBytes(path, original);
        var source = Path.Combine(_root, "prepared.pkn");
        File.WriteAllBytes(source, replacement);
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Resource plugin" });
        GameResourceChange[] claims = [
            new("sample.models", "1.0.0", name, "Models/Body.msh", "replace", Hash(original), Hash(replacement)),
            new("sample.items", "2.0.0", name, "Models/Item.msh", "add", null, Hash(replacement))];
        var payload = new FilePluginPayload(source, target, replacement.Length, Hash(replacement))
        {
            CacheReused = true,
            Changes = claims
        };
        Assert.Null(ResourceOverrideDiagnostics.Read(store, "test"));
        ResourceOverrideDiagnostics.Capture(store, "test", [payload]);
        var captured = Assert.Single(ResourceOverrideDiagnostics.Read(store, "test")!.Files);
        Assert.Equal("original", captured.State);
        Assert.True(captured.CacheReused);
        Assert.Equal(claims.Select(change => (change.PluginId, change.Version, change.Member, change.Action,
                change.OriginalSha256, change.Sha256, Label: change.Name.Resolve("en"))),
            captured.Changes.Select(change => (change.PluginId, change.Version, change.Member, change.Action,
                change.OriginalSha256, change.Sha256, Label: change.Name.Resolve("en"))));
        Assert.Equal(original, File.ReadAllBytes(path));
        var transaction = RuntimeDeployment.Apply(client, "sample", null, [payload]);
        Assert.Equal("overridden", Assert.Single(ResourceOverrideDiagnostics.Read(store, "test")!.Files).State);
        Assert.Equal(replacement, File.ReadAllBytes(path));
        transaction.Restore();
        Assert.Equal("original", Assert.Single(ResourceOverrideDiagnostics.Read(store, "test")!.Files).State);
        File.WriteAllText(path, "externally changed");
        Assert.Equal("modified", Assert.Single(ResourceOverrideDiagnostics.Read(store, "test")!.Files).State);
        Assert.Equal("externally changed", File.ReadAllText(path));
        ResourceOverrideDiagnostics.Capture(store, "test", []);
        Assert.Empty(ResourceOverrideDiagnostics.Read(store, "test")!.Files);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
