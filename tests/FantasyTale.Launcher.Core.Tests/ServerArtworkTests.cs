using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Host;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerArtworkTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-server-artwork-").FullName;
    private static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task ImportedImagesSurviveTheOriginalAndResolveFromOnlyTheManagedResourceRoot()
    {
        var media = Path.Combine(_root, "server-media");
        var source = Path.Combine(_root, "selected image.png"); await File.WriteAllBytesAsync(source, Image);
        var reference = await new ServerArtworkStore(media).ImportAsync(source);
        Assert.True(ServerArtworkStore.IsReference(reference));
        File.Delete(source);
        var origin = "https://fantasytale.invalid";
        using var stream = StaticFiles.Open(_root, _root, origin, origin + reference, out var type, media);
        Assert.Equal("image/png", type);
        using var contents = new MemoryStream(); await stream!.CopyToAsync(contents);
        Assert.Equal(Image, contents.ToArray());
        Assert.Null(StaticFiles.Open(_root, _root, origin, "https://other.example" + reference, out _, media));
        using var missing = StaticFiles.Open(_root, _root, origin, origin + reference, out _);
        Assert.Equal(0, missing!.Length);
    }

    [Fact]
    public async Task IdenticalImagesShareOneReferenceAndMixedNetworkAndLocalMetadataPersist()
    {
        var media = Path.Combine(_root, "media"); var store = new ServerArtworkStore(media);
        var source = Path.Combine(_root, "one.png"); await File.WriteAllBytesAsync(source, Image);
        var first = await store.ImportAsync(source);
        var renamed = Path.Combine(_root, "two.jpeg"); File.Copy(source, renamed);
        Assert.Equal(first, await store.ImportAsync(renamed));
        Assert.EndsWith(".png", first);
        Assert.Single(Directory.GetFiles(media));
        var servers = new ServerStore(Path.Combine(_root, "servers"));
        servers.Save(new("fixture", "test", "Fixture", null, null, false, false)
        { Presentation = new() { IconUrl = first, Images = [first, "https://images.example/gallery.jpg"], RosterEnabled = false } });
        var game = new GameDefinition("test", GameStatus.Enabled, new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" }), "Test", "external", "#0078d4", null, _root, []);
        var saved = Assert.Single(new ServerStore(Path.Combine(_root, "servers")).List([game]));
        Assert.Equal(first, saved.Presentation!.IconUrl);
        Assert.Equal([first, "https://images.example/gallery.jpg"], saved.Presentation.Images);
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(saved.Presentation));
    }

    [Fact]
    public async Task NonImagesAndCanceledImportsLeaveNoPartialMediaFiles()
    {
        var media = Path.Combine(_root, "media"); var store = new ServerArtworkStore(media);
        var source = Path.Combine(_root, "fake.png"); await File.WriteAllTextAsync(source, "not an image");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(source));
        Assert.Empty(Directory.GetFiles(media));
        await File.WriteAllBytesAsync(source, Image);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(source, canceled.Token));
        Assert.Empty(Directory.GetFiles(media));
    }

    [Theory]
    [InlineData("/server-media/../secret.png")]
    [InlineData("/server-media/.import-pending")]
    [InlineData("/server-media/session.toml")]
    [InlineData("/server-media/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.svg")]
    [InlineData("/server-media/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png?path=secret")]
    public void InvalidManagedReferencesNeverExposeUnrelatedLocalFiles(string reference)
    {
        Assert.False(ServerArtworkStore.IsReference(reference));
        var media = Path.Combine(_root, "media"); Directory.CreateDirectory(media);
        File.WriteAllText(Path.Combine(media, "session.toml"), "secret");
        using var stream = StaticFiles.Open(_root, _root, "https://fantasytale.invalid/", "https://fantasytale.invalid" + reference, out _, media);
        Assert.Equal(0, stream!.Length);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
