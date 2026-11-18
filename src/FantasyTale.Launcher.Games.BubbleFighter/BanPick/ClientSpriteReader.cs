using FantasyTale.Launcher.Games.BubbleFighter.Archives;

namespace FantasyTale.Launcher.Games.BubbleFighter.BanPick;

/// <summary>A four-state button sheet read from the running client's own data.</summary>
/// <param name="Png">The sheet image as the client stores it.</param>
/// <param name="Frames">Four inclusive <c>left, top, right, bottom</c> rectangles: up, down, hover, disabled.</param>
internal sealed record ClientSprite(byte[] Png, int[][] Frames);

/// <summary>Finds a sprite (<c>.png</c> plus its <c>.smf</c> frame table) in the client's loose files or archives.</summary>
internal static class ClientSpriteReader
{
    /// <summary>Reads <paramref name="member"/> (without extension) from <paramref name="clientFolder"/>, or null when this client does not ship it.</summary>
    internal static ClientSprite? TryRead(string clientFolder, string member, CancellationToken stop)
    {
        var png = ReadMember(clientFolder, member + ".png", stop);
        var smf = png is null ? null : ReadMember(clientFolder, member + ".smf", stop);
        if (png is null || smf is null) return null;
        return ParseFrames(smf) is { } frames ? new(png, frames) : null;
    }

    /// <summary>Parses an SMF version-2 frame table of exactly four 32-byte records, as the select-map sheet has.</summary>
    internal static int[][]? ParseFrames(byte[] smf)
    {
        if (smf.Length != 8 + 4 * 32 || BitConverter.ToUInt32(smf, 0) != 2 || BitConverter.ToUInt32(smf, 4) != 4) return null;
        var frames = new int[4][];
        for (var index = 0; index < 4; index++)
        {
            frames[index] = [BitConverter.ToInt32(smf, 8 + index * 32), BitConverter.ToInt32(smf, 12 + index * 32),
                BitConverter.ToInt32(smf, 16 + index * 32), BitConverter.ToInt32(smf, 20 + index * 32)];
            if (frames[index][2] < frames[index][0] || frames[index][3] < frames[index][1] || frames[index].Any(value => value < 0)) return null;
        }
        return frames;
    }

    private static byte[]? ReadMember(string root, string member, CancellationToken stop)
    {
        var loose = Path.Combine(root, member.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose)) return File.ReadAllBytes(loose);
        foreach (var file in Archives(root))
        {
            stop.ThrowIfCancellationRequested();
            try
            {
                using var archive = Archive.Open(file, Path.GetRelativePath(root, file).Replace('\\', '/'));
                if (archive.Contains(member)) return archive.Read(member);
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    // Image packs first: the sheet lives in the build's Images archive, and opening every other pack would only cost time.
    private static IEnumerable<string> Archives(string root)
    {
        var all = new List<string>();
        foreach (var pattern in new[] { "*.pkn", "*.bfz" })
            try { all.AddRange(Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return all.OrderByDescending(file => Path.GetFileName(file).Contains("Image", StringComparison.OrdinalIgnoreCase))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase);
    }
}
