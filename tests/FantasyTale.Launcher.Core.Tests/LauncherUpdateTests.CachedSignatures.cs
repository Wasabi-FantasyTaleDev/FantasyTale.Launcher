using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData("http", "valid")]
    [InlineData("alist", "valid")]
    [InlineData("github", "valid")]
    [InlineData("http", "missing")]
    [InlineData("alist", "missing")]
    [InlineData("github", "missing")]
    [InlineData("http", "corrupt")]
    [InlineData("alist", "corrupt")]
    [InlineData("github", "corrupt")]
    [InlineData("http", "rotated-key")]
    [InlineData("alist", "rotated-key")]
    [InlineData("github", "rotated-key")]
    [InlineData("http", "new-signature-policy")]
    [InlineData("alist", "new-signature-policy")]
    [InlineData("github", "new-signature-policy")]
    public async Task RestoredTransferMustAuthenticateItsSavedIndexUnderTheCurrentPolicy(string kind, string scenario)
    {
        using var key = RSA.Create(2048);
        using var replacement = RSA.Create(2048);
        Policy(kind);
        if (scenario != "new-signature-policy") RequireSignature(key);
        var index = IndexBytes(Manifest());
        var signature = key.SignData(index, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var handler = new SignedSourceHandler(index, signature);
        using var http = new HttpClient(handler);
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(false, false));
            var available = await first.CheckAsync();
            await first.DownloadAsync(available.ManifestSha256!);
            await UntilAsync(() => first.State.Status is "ready" or "failed");
            Assert.Equal("ready", first.State.Status);
        }
        var signaturePath = Path.Combine(Data, "launcher-update", "release.toml.sig");
        if (scenario == "valid") Assert.Equal(signature, File.ReadAllBytes(signaturePath));
        else if (scenario == "missing") File.Delete(signaturePath);
        else if (scenario == "corrupt") File.WriteAllBytes(signaturePath, new byte[signature.Length]);
        else if (scenario == "rotated-key") { Policy(kind); RequireSignature(replacement); }
        else if (scenario == "new-signature-policy") RequireSignature(key);
        using var offline = new HttpClient(new FeedHandler(index) { Unavailable = true });
        await using var restored = new LauncherUpdateService(Installation, Data, offline);
        restored.StartMonitoring();
        await UntilAsync(() => restored.State.Status is "ready" or "failed");
        Assert.Equal(scenario == "valid" ? "ready" : "failed", restored.State.Status);
        if (scenario != "valid") Assert.Empty(restored.List());
        Assert.Equal(2, handler.PayloadRequests);
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("alist")]
    [InlineData("github")]
    public async Task StagingRejectsASignatureChangedAfterDiscoveryBeforeRequestingPayloads(string kind)
    {
        using var key = RSA.Create(2048);
        Policy(kind); RequireSignature(key);
        var index = IndexBytes(Manifest());
        var signature = key.SignData(index, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var handler = new SignedSourceHandler(index, signature);
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var available = await service.CheckAsync();
        File.WriteAllBytes(Path.Combine(Data, "launcher-update", "release.toml.sig"), new byte[signature.Length]);
        await service.DownloadAsync(available.ManifestSha256!);
        await UntilAsync(() => service.State.Status is "ready" or "failed");
        Assert.Equal("failed", service.State.Status);
        Assert.Equal("integrity", service.State.Failure);
        Assert.Equal(0, handler.PayloadRequests);
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
    }

    private void RequireSignature(RSA key) => File.AppendAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"""

        signature_path = "launcher/stable.toml.sig"
        signature_public_key = "{Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}"
        """);
}
