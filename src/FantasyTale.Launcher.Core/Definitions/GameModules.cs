using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>Discovers installed managed modules solely through their public SDK contract.</summary>
public static class GameModules
{
    private static readonly ConcurrentDictionary<string, Lazy<Assembly>> Assemblies = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private sealed class ModuleLoadContext(string path) : AssemblyLoadContext("game-module:" + path)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        private readonly string _folder = Path.GetDirectoryName(path)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // All modules exchange SDK objects with the host, including when their package contains an SDK copy.
            var sdk = typeof(IGameModule).Assembly;
            if (assemblyName.Name == sdk.GetName().Name) return sdk;
            // Self-contained packages carry framework DLLs beside modules. Loading them privately splits
            // exception and serialization identities across callbacks into the host.
            if (assemblyName.Name is { } framework && (framework is "System" or "mscorlib" or "netstandard" or "WindowsBase"
                or "Microsoft.CSharp" or "Microsoft.VisualBasic" or "Microsoft.VisualBasic.Core"
                || framework.StartsWith("System.", StringComparison.Ordinal)
                || framework.StartsWith("Microsoft.Win32.", StringComparison.Ordinal)))
            {
                try { return Default.LoadFromAssemblyName(assemblyName); }
                catch (FileNotFoundException) { /* A package may provide an additional framework-named dependency. */ }
                catch (FileLoadException error)
                {
                    // A present but unloadable host assembly must not fall back to a private copy;
                    // that would split type identity. Preserve the loader's actual reason.
                    throw new FileLoadException(
                        $"The game module requires {assemblyName.FullName}, which the host runtime could not load. Check the module's runtime requirements and update or repair the launcher.",
                        assemblyName.FullName, error);
                }
            }
            var dependency = _resolver.ResolveAssemblyToPath(assemblyName);
            if (dependency is null && assemblyName.Name is { } name && name == Path.GetFileName(name))
            {
                var adjacent = Path.Combine(_folder, name + ".dll");
                if (File.Exists(adjacent)) dependency = adjacent;
            }
            return dependency is null ? null : LoadFromAssemblyPath(dependency);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var dependency = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return dependency is null ? 0 : LoadUnmanagedDllFromPath(dependency);
        }
    }

    /// <summary>Loads the game-local assembly, falling back to the application's bundled module directory.</summary>
    public static IGameModule Load(GameDefinition game, string applicationFolder)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Status != GameStatus.Enabled) throw new InvalidOperationException("The game module is not installed.");
        if (game.Module.Length == 0 || game.Module != Path.GetFileName(game.Module) || game.Module.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new DefinitionException(Path.Combine(game.Folder, "game.toml"), "game.module", "must be an assembly file name");
        var path = Path.Combine(game.Folder, game.Module);
        if (!File.Exists(path)) path = Path.Combine(applicationFolder, game.Module);
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The game module assembly is not installed.", path);
        var assembly = Assemblies.GetOrAdd(path, file => new Lazy<Assembly>(
            () => new ModuleLoadContext(file).LoadFromAssemblyPath(file))).Value;
        var types = assembly.GetExportedTypes().Where(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToArray();
        if (types.Length != 1) throw new InvalidOperationException("The game assembly must expose exactly one game module.");
        var module = Activator.CreateInstance(types[0]) as IGameModule ?? throw new InvalidOperationException("The game module needs a public parameterless constructor.");
        if (module.GameId != game.Id) throw new InvalidOperationException("The module serves a different game.");
        return module;
    }
}
