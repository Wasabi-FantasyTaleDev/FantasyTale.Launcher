using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class GameModuleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-module-").FullName;
    private const string ModuleFile = "FantasyTale.Launcher.ModuleFixture.dll";
    private const string DependencyFile = "FantasyTale.Launcher.ModuleDependency.dll";
    private sealed record Request(string Folder, string ApplicationFolder, string Id = "module-fixture", string Module = ModuleFile, bool Enabled = true, bool Invoke = true);

    private string Package(string name, bool dependencies = true, bool manifest = true)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "module-fixture")))
        {
            if (!dependencies && Path.GetFileName(file) == DependencyFile || !manifest && file.EndsWith(".deps.json", StringComparison.Ordinal)) continue;
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        return folder;
    }

    private async Task<JsonElement[]> Probe(params Request[] requests)
    {
        var input = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        var output = input + ".result";
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(requests));
        // Loaded DLLs remain mapped until the process exits, matching an installed module's lifetime.
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "test-client", "FantasyTale.Launcher.TestClient.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WorkingDirectory = _root,
        };
        foreach (var argument in new[] { "--module-probe", input, output }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await errors);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            return result.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    private static void Successful(JsonElement result)
    {
        Assert.Equal(JsonValueKind.Null, result.GetProperty("Error").ValueKind);
        Assert.True(result.GetProperty("SharedSdk").GetBoolean());
        Assert.False(result.GetProperty("DefaultContext").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GameLocalDependenciesResolveWithOrWithoutAComponentManifestAndShareTheHostSdk(bool manifest)
    {
        var folder = Package("game", manifest: manifest);
        Assert.True(File.Exists(Path.Combine(folder, "FantasyTale.Launcher.Sdk.dll")));
        var result = Assert.Single(await Probe(new Request(folder, _root)));
        Successful(result);
        Assert.Equal(folder, result.GetProperty("DependencyFolder").GetString());
    }

    [Fact]
    public async Task AdjacentFrameworkCopiesPreserveHostNetworkExceptionIdentity()
    {
        var folder = Package("framework-copies");
        File.Copy(typeof(HttpRequestException).Assembly.Location, Path.Combine(folder, "System.Net.Http.dll"));
        File.WriteAllText(Path.Combine(folder, "http-probe"), "catch-host-network-failure");
        var result = Assert.Single(await Probe(new Request(folder, _root)));
        Successful(result);
        Assert.Equal(folder, result.GetProperty("DependencyFolder").GetString());
    }

    [Fact]
    public async Task SameNamedAssembliesRemainPackageLocalAndConcurrentRepeatedLoadsReuseTheirContext()
    {
        var first = Package("first");
        var second = Package("second");
        var requests = Enumerable.Range(0, 12).Select(index => new Request(index % 2 == 0 ? first : second, _root)).ToArray();
        var results = await Probe(requests);
        foreach (var (request, result) in requests.Zip(results))
        {
            Successful(result);
            Assert.Equal(request.Folder, result.GetProperty("DependencyFolder").GetString());
            Assert.Equal(request.Folder, Path.GetDirectoryName(result.GetProperty("AssemblyPath").GetString()));
        }
        Assert.Equal(2, results.Select(result => result.GetProperty("Context").GetInt32()).Distinct().Count());
        Assert.Equal(2, results.Select(result => result.GetProperty("Assembly").GetInt32()).Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeDependenciesExecuteFromTheirOwnPackageWithOrWithoutAComponentManifest(bool manifest)
    {
        if (!OperatingSystem.IsWindows()) return;
        var first = Package("first-native", manifest: manifest);
        var second = Package("second-native", manifest: manifest);
        var decoder = Path.Combine(AppContext.BaseDirectory, "archive", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "7z.dll");
        foreach (var directory in new[] { first, second })
        {
            File.Copy(decoder, Path.Combine(directory, "fixture-native.dll"));
            File.WriteAllText(Path.Combine(directory, "native-probe"), "query-formats");
        }
        var results = await Probe(new(first, _root), new(second, _root));
        Assert.All(results, Successful);
        Assert.All(results, result => Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), result.GetProperty("Architecture").GetString()));
        Assert.Equal(first, results[0].GetProperty("DependencyFolder").GetString());
        Assert.Equal(second, results[1].GetProperty("DependencyFolder").GetString());
    }

    [Fact]
    public async Task MissingNativeDependencyFailsWithoutStartingAGame()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Package("missing-native");
        File.WriteAllText(Path.Combine(folder, "native-probe"), "query-formats");
        var result = Assert.Single(await Probe(new Request(folder, _root)));
        Assert.Equal(nameof(DllNotFoundException), result.GetProperty("Error").GetString());
    }

    [Fact]
    public async Task InstalledGameModuleTakesPrecedenceAndApplicationFallbackResolvesItsOwnDependencies()
    {
        var application = Package("application");
        var game = Package("game");
        var absent = Directory.CreateDirectory(Path.Combine(_root, "absent")).FullName;
        var results = await Probe(new(game, application), new(absent, application));
        Assert.All(results, Successful);
        Assert.Equal(game, results[0].GetProperty("DependencyFolder").GetString());
        Assert.Equal(application, results[1].GetProperty("DependencyFolder").GetString());
    }

    [Fact]
    public async Task MissingPrivateDependenciesFailWhileAnotherPackageLoadsItsOwnCopy()
    {
        var complete = Package("complete");
        var incomplete = Package("incomplete", dependencies: false);
        var results = await Probe(new(complete, _root), new(incomplete, _root));
        Successful(results[0]);
        Assert.Equal(complete, results[0].GetProperty("DependencyFolder").GetString());
        Assert.Equal(nameof(FileNotFoundException), results[1].GetProperty("Error").GetString());
    }

    [Fact]
    public async Task PlaceholderMissingAssemblyAndMismatchedGameIdentityRemainErrors()
    {
        var game = Package("game");
        var results = await Probe(new(game, _root, Enabled: false), new(game, _root, Module: "../outside.dll"),
            new(game, _root, Id: "other-game"), new(game, _root, Module: "missing.dll"));
        Assert.Equal([nameof(InvalidOperationException), nameof(DefinitionException), nameof(InvalidOperationException), nameof(FileNotFoundException)],
            results.Select(result => result.GetProperty("Error").GetString()!).ToArray());
        Assert.Equal(Path.Combine(_root, "missing.dll"), results[3].GetProperty("ErrorFile").GetString());
    }

    [Fact]
    public async Task ExistingBundledModuleRetainsItsOptionalHostingInterfaceThroughTheSharedSdk()
    {
        var result = Assert.Single(await Probe(new Request(_root, AppContext.BaseDirectory, "bubblefighter", "FantasyTale.Launcher.Games.BubbleFighter.dll", Invoke: false)));
        Successful(result);
        Assert.True(result.GetProperty("LocalHosting").GetBoolean());
    }

    public void Dispose() => Directory.Delete(_root, true);
}
