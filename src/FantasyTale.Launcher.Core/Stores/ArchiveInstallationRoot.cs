namespace FantasyTale.Launcher.Core.Stores;

internal static class ArchiveInstallationRoot
{
    internal static IReadOnlyList<ClientReleaseFile> Find(IReadOnlyList<ClientReleaseFile> files, IEnumerable<string> executables)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var executable in executables.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = ClientRelease.ValidatePath(executable);
            foreach (var file in files)
            {
                if (file.Path.Equals(executable, StringComparison.OrdinalIgnoreCase)) roots.Add("");
                else if (file.Path.EndsWith("/" + executable, StringComparison.OrdinalIgnoreCase))
                    roots.Add(file.Path[..^executable.Length]);
            }
        }
        if (roots.Count != 1) throw new InvalidDataException(roots.Count == 0
            ? "Archive does not contain the configured executable."
            : "Archive contains multiple installation roots matching the configured executable.");
        var prefix = roots.Single();
        return files.Where(file => file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(file => file with { Path = ClientRelease.ValidatePath(file.Path[prefix.Length..]) }).ToArray();
    }
}
