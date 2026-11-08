using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>A verified client root and the builds whose executable identity matches there.</summary>
public sealed record DiscoveredClientInstallation(string Path, IReadOnlyList<string> BuildIds);

/// <summary>Finds existing clients below a selected directory without following links or guessing between multiple installations.</summary>
public static class ClientInstallationDiscovery
{
    /// <summary>Searches at most eight directory levels and 4096 directories; returns null for an empty or unknown installation.</summary>
    public static DiscoveredClientInstallation? Find(string folder, IReadOnlyList<BuildDefinition> builds)
    {
        var root = System.IO.Path.GetFullPath(folder);
        InstanceStore.RejectLinks(root);
        if (!Directory.Exists(root)) return null;
        var pending = new Queue<(string Folder, int Depth)>(); pending.Enqueue((root, 0));
        var matches = new List<DiscoveredClientInstallation>();
        var directories = 0; var entries = 0;
        while (pending.TryDequeue(out var current))
        {
            if (++directories > 4096) throw new IOException("client_search_limit");
            var ids = builds.Where(build => ClientExecutable.FindInstalled(current.Folder, build) is not null).Select(build => build.Id).ToArray();
            if (ids.Length > 0)
            {
                matches.Add(new(current.Folder, ids));
                if (matches.Count > 1) throw new IOException("client_search_multiple");
                // A valid selected root owns its resources; do not search its data folders.
                if (current.Depth == 0) return matches[0];
                continue;
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(current.Folder))
            {
                if (++entries > 100000) throw new IOException("client_search_limit");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (current.Depth == 8) throw new IOException("client_search_limit");
                pending.Enqueue((path, current.Depth + 1));
            }
        }
        return matches.SingleOrDefault();
    }
}
