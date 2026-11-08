namespace FantasyTale.Launcher.Sdk;

/// <summary>A rectangle in a local PNG sprite sheet, with dimensions needed to render it without stretching.</summary>
/// <param name="Path">Image path relative to the game folder.</param>
/// <param name="X">Left coordinate in source pixels.</param>
/// <param name="Y">Top coordinate in source pixels.</param>
/// <param name="Width">Visible icon width in source pixels.</param>
/// <param name="Height">Visible icon height in source pixels.</param>
/// <param name="SheetWidth">Full PNG width.</param>
/// <param name="SheetHeight">Full PNG height.</param>
public sealed record LevelIcon(string Path, int X, int Y, int Width, int Height, int SheetWidth, int SheetHeight);

/// <summary>One original level identifier, its inclusive cumulative experience boundary and configured icon variants.</summary>
/// <param name="Level">Original level id, preserved even when numbering starts at zero.</param>
/// <param name="MinimumExperience">Total experience required to enter this level, using 64-bit arithmetic.</param>
/// <param name="Name">Original name when supplied; null leaves the numeric label visible.</param>
/// <param name="Icons">Icon variants; default is the ordinary player icon.</param>
public sealed record LevelEntry(int Level, long MinimumExperience, string? Name, IReadOnlyDictionary<string, LevelIcon> Icons);

/// <summary>Validated level data loaded from replaceable tables and images, independently of a game module.</summary>
/// <param name="Entries">Levels ordered by original id with increasing experience boundaries.</param>
/// <param name="Badges">Explicit account badges such as piero, separate from experience progression.</param>
/// <param name="DisplayOffset">Offset added only to a visible level label, never to account ids or thresholds.</param>
/// <param name="NamesPending">Whether original level names are awaiting documentation.</param>
public sealed record LevelCatalogDefinition(IReadOnlyList<LevelEntry> Entries, IReadOnlyDictionary<string, LevelBadge> Badges,
    int DisplayOffset, bool NamesPending);

/// <summary>An account icon override that preserves the player's ordinary level and experience.</summary>
/// <param name="IconId">Reserved presentation id, independent of the player's level.</param>
/// <param name="Name">Display label for the special rank.</param>
/// <param name="Icon">Image rectangle for the special rank.</param>
public sealed record LevelBadge(int IconId, string Name, LevelIcon Icon);
