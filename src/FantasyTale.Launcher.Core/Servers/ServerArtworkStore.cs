using System.Buffers;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Copies user-selected server images into an independent local media library.</summary>
public sealed class ServerArtworkStore
{
    /// <summary>Reserved relative URL prefix for imported server images.</summary>
    public const string UrlPrefix = "/server-media/";
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create("0123456789abcdef");
    private readonly string _root;

    /// <summary>Creates the local image library without allowing linked destination directories.</summary>
    public ServerArtworkStore(string root)
    {
        _root = Path.GetFullPath(root);
        InstanceStore.RejectLinks(_root);
        Directory.CreateDirectory(_root);
    }

    /// <summary>Recognizes only content-addressed raster image references, without exposing arbitrary local paths.</summary>
    public static bool IsReference(string value)
    {
        if (!value.StartsWith(UrlPrefix, StringComparison.Ordinal)) return false;
        var name = value.AsSpan(UrlPrefix.Length);
        if (name.Length < 68 || name[64] != '.' || name[..64].ContainsAnyExcept(HexCharacters)) return false;
        return name[64..] is ".png" or ".jpg" or ".webp" or ".gif" or ".bmp" or ".ico";
    }

    /// <summary>Imports a raster image by its content and returns a stable reference that survives moving the original file.</summary>
    public async Task<string> ImportAsync(string source, CancellationToken cancellationToken = default)
    {
        InstanceStore.RejectLinks(source);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[16];
        var read = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        var extension = ImageExtension(header.AsSpan(0, read));
        input.Position = 0;
        var temporary = Path.Combine(_root, ".import-" + Guid.NewGuid().ToString("N"));
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var name = Convert.ToHexStringLower(hash.GetHashAndReset()) + extension;
            var destination = Path.Combine(_root, name);
            InstanceStore.RejectLinks(destination);
            File.Move(temporary, destination, overwrite: true);
            return UrlPrefix + name;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ImageExtension(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (header.StartsWith(new byte[] { 255, 216, 255 })) return ".jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return ".gif";
        if (header.StartsWith("BM"u8)) return ".bmp";
        if (header.StartsWith(new byte[] { 0, 0, 1, 0 })) return ".ico";
        throw new InvalidDataException("server_image_format_unsupported");
    }
}
