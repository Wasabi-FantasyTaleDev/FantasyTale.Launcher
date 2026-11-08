using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule
{
    /// <summary>Reads optional version.dat labels; missing or unreadable metadata never prevents starting the client.</summary>
    public ClientVersionMetadata ReadVersionMetadata(BuildDefinition build, string instanceFolder)
    {
        var fallback = new ClientVersionMetadata(build.ReleaseVersion, build.PatchVersion);
        try
        {
            var path = Path.Combine(instanceFolder, "version.dat");
            if (!File.Exists(path))
            {
                path = Directory.EnumerateFiles(instanceFolder).Take(4096).FirstOrDefault(file =>
                    Path.GetFileName(file).Equals("version.dat", StringComparison.OrdinalIgnoreCase));
                if (path is null) return fallback;
            }
            using var stream = File.OpenRead(path);
            if (stream.Length > 16384) return fallback;
            using var reader = new StreamReader(stream);
            var text = new char[16385];
            var length = reader.ReadBlock(text, 0, text.Length);
            if (length > 16384) return fallback;
            string? common = null, patch = null;
            var section = "";
            foreach (var raw in new string(text, 0, length).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;
                if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
                var separator = line.IndexOf('=');
                if (separator < 0 || !line[..separator].Trim().Equals("VERSION", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(separator + 1)..].Trim();
                if (value.Length is 0 or > 64 || value.Any(c => !char.IsAsciiDigit(c) && c != '.')) continue;
                if (section.Equals("PATCH", StringComparison.OrdinalIgnoreCase)) patch = value;
                else if (section.Equals("Common", StringComparison.OrdinalIgnoreCase)) common = value;
            }
            return new(common ?? fallback.ReleaseVersion, patch ?? common ?? fallback.PatchVersion);
        }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
    }
}
