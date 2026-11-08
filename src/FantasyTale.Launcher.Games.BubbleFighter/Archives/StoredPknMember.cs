namespace FantasyTale.Launcher.Games.BubbleFighter.Archives;

internal sealed record StoredPknMember(string Path, uint Flags, uint Size, uint StoredSize, byte[] Md5, byte[] Payload);
internal sealed record PknWriteMember(string Path, byte[]? Data, StoredPknMember? Stored)
{
    internal StoredBfzMember? Bfz { get; init; }
}

internal static class StoredPkn
{
    internal static StoredPknMember Read(IArchiveReader archive, string path) => archive switch
    {
        PknArchive modern => modern.ReadStored(path),
        LegacyPknArchive early => early.ReadStored(path),
        _ => throw new InvalidOperationException("The archive is not a PKN pack.")
    };
}
