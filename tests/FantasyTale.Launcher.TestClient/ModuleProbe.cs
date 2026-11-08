using System.Collections.Concurrent;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

internal static class ModuleProbe
{
    private sealed record Request(string Folder, string ApplicationFolder, string Id, string Module, bool Enabled, bool Invoke);

    internal static int Run(string input, string output)
    {
        var requests = JsonSerializer.Deserialize<Request[]>(File.ReadAllText(input))
            ?? throw new InvalidDataException("Module probe requests are missing.");
        var contexts = new ConcurrentDictionary<object, int>(ReferenceEqualityComparer.Instance);
        var assemblies = new ConcurrentDictionary<object, int>(ReferenceEqualityComparer.Instance);
        var nextContext = 0;
        var nextAssembly = 0;
        var results = requests.AsParallel().AsOrdered().Select(request =>
        {
            try
            {
                var game = new GameDefinition(request.Id, request.Enabled ? GameStatus.Enabled : GameStatus.Placeholder,
                    new(new Dictionary<string, string> { ["en"] = "Module fixture" }), request.Module, "external", "", null, request.Folder, []);
                var module = GameModules.Load(game, request.ApplicationFolder);
                var assembly = module.GetType().Assembly;
                var context = AssemblyLoadContext.GetLoadContext(assembly)!;
                var build = new BuildDefinition("fixture-1", "fixture", 1, game.Name, "fixture.exe", "", "", null, null, "none", [], []);
                var directory = request.Invoke ? module.CreateLaunchPlan(new(game, build, game.Folder, null, null)
                    { AutomaticSelection = new NetworkFailureSelection() }).WorkingDirectory : null;
                return new
                {
                    Error = (string?)null, ErrorFile = (string?)null, ErrorDetail = (string?)null, AssemblyPath = (string?)assembly.Location,
                    DependencyFolder = directory, SharedSdk = module.GetType().GetInterfaces().Contains(typeof(IGameModule)),
                    DefaultContext = context == AssemblyLoadContext.Default,
                    Context = contexts.GetOrAdd(context, _ => Interlocked.Increment(ref nextContext)),
                    Assembly = assemblies.GetOrAdd(assembly, _ => Interlocked.Increment(ref nextAssembly)),
                    LocalHosting = module is ILocalServerModule,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                };
            }
            catch (Exception error)
            {
                return new
                {
                    Error = (string?)error.GetType().Name, ErrorFile = (error as FileNotFoundException)?.FileName,
                    ErrorDetail = (string?)error.ToString(),
                    AssemblyPath = (string?)null, DependencyFolder = (string?)null, SharedSdk = false,
                    DefaultContext = false, Context = 0, Assembly = 0, LocalHosting = false,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                };
            }
        }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(results));
        return 0;
    }

    private sealed class NetworkFailureSelection : IAutomaticServerSelection
    {
        public LaunchServerSelection Grant => throw new NotSupportedException();
        public IReadOnlyList<GameServerTarget> Targets => [];
        public ServerSelector Automatic => throw new NotSupportedException();
        public IReadOnlyList<string> AdmittedCenterIds => [];
        public Task<IReadOnlyList<AutomaticServerCandidate>?> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<AutomaticServerCandidate>?>(new ServerFailureException("network_error"));
    }
}
