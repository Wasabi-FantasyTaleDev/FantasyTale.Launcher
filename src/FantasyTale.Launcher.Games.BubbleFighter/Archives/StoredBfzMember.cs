namespace FantasyTale.Launcher.Games.BubbleFighter.Archives;

internal sealed record StoredBfzMember(string Path, byte[] Record, byte[] Central);
internal sealed record BfzWriteMember(string Path, byte[]? Data, StoredBfzMember? Stored);
