using System.Formats.Asn1;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

namespace FantasyTale.Launcher.Sdk;

/// <summary>A publisher certificate and timestamp extracted from one embedded signature; extraction does not verify authenticity.</summary>
/// <param name="CertificateSha256">SHA-256 of the signer certificate, independent of client file contents.</param>
/// <param name="Timestamp">Timestamp-authority time, not the signer's self-declared signing time.</param>
public sealed record ClientSignerIdentity(string CertificateSha256, DateTimeOffset Timestamp);

/// <summary>Shared release-identification evidence for launch planning and plugin build selection.</summary>
/// <param name="PeTimestamp">Linker timestamp retained by the inspected packed and unpacked client pairs.</param>
/// <param name="Signers">Embedded publisher/timestamp pairs; these are metadata, not a trust-verification result.</param>
/// <param name="SignatureMetadataError">A malformed or stale certificate-table error, preserved for unsigned-fallback diagnostics.</param>
public sealed record ClientExecutableIdentity(uint PeTimestamp, IReadOnlyList<ClientSignerIdentity> Signers, string? SignatureMetadataError);

/// <summary>Identifies executable candidates from build data without whole-file hashes or executing client code.</summary>
public static class ClientExecutable
{
    private const int MaximumCertificateBytes = 16 * 1024 * 1024;
    private const int MaximumSignatures = 16;

    /// <summary>Returns contained candidate paths in configured order, rejecting linked paths and duplicate names.</summary>
    public static IReadOnlyList<string> CandidatePaths(string folder, BuildDefinition build)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var paths = new List<string>();
        foreach (var name in new[] { build.Executable }.Concat(build.ExecutableAliases))
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            if (Path.IsPathRooted(name) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build {build.Id}: executable candidates must stay inside the client directory.");
            for (var current = path; current is not null; current = Path.GetDirectoryName(current))
                if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked executable candidates are not supported.");
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        }
        return paths;
    }

    /// <summary>Finds the first installed candidate matching the release; absence or mismatched evidence returns null.</summary>
    public static string? FindInstalled(string folder, BuildDefinition build)
    {
        foreach (var path in CandidatePaths(folder, build))
        {
            if (!File.Exists(path)) continue;
            if (build.PeTimestamp is null && build.SignerCertificateSha256 is null) return path;
            if (Matches(ReadIdentity(path), build)) return path;
        }
        return null;
    }

    /// <summary>Matches signed publisher/time data first; unsigned or stale-signature candidates require the configured PE fingerprint.</summary>
    public static bool Matches(ClientExecutableIdentity identity, BuildDefinition build)
    {
        if (build.PeTimestamp is { } timestamp && identity.PeTimestamp != timestamp) return false;
        if (build.SignerCertificateSha256 is { } signer && build.SignedAt is { } signedAt && identity.Signers.Count > 0)
            return identity.Signers.Any(value => value.CertificateSha256.Equals(signer, StringComparison.OrdinalIgnoreCase) && value.Timestamp == signedAt);
        return build.PeTimestamp.HasValue;
    }

    /// <summary>Reads bounded PE and Authenticode identification metadata; malformed signature tables retain usable header evidence.</summary>
    public static ClientExecutableIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var header = image.PEHeaders.PEHeader ?? throw new InvalidDataException("The candidate has no PE optional header.");
        var timestamp = unchecked((uint)image.PEHeaders.CoffHeader.TimeDateStamp);
        var table = header.CertificateTableDirectory;
        if (table.RelativeVirtualAddress == 0 && table.Size == 0) return new(timestamp, [], null);
        try
        {
            if (table.Size < 8 || table.Size > MaximumCertificateBytes || table.RelativeVirtualAddress < 0 || (long)table.RelativeVirtualAddress + table.Size > stream.Length)
                throw new InvalidDataException("The certificate table is invalid or exceeds its inspection limit.");
            stream.Position = table.RelativeVirtualAddress;
            var bytes = new byte[table.Size]; stream.ReadExactly(bytes);
            var signers = new List<ClientSignerIdentity>();
            var count = 0;
            for (var offset = 0; offset < bytes.Length;)
            {
                if (++count > MaximumSignatures || bytes.Length - offset < 8) throw new InvalidDataException("Invalid certificate record count or size.");
                var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
                if (size < 8 || size > bytes.Length - offset || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2)) != 0x200
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 6, 2)) != 2)
                    throw new InvalidDataException("Invalid or stale WIN_CERTIFICATE record.");
                var payload = bytes.AsMemory(offset + 8, size - 8);
                var reader = new AsnReader(payload, AsnEncodingRules.BER);
                var encoded = reader.ReadEncodedValue();
                if (payload.Span[encoded.Length..].ContainsAnyExcept((byte)0)) throw new InvalidDataException("Nonzero data follows the embedded signature.");
                var signature = new SignedCms(); signature.Decode(encoded.ToArray());
                if (signature.SignerInfos.Count > MaximumSignatures) throw new InvalidDataException("Too many embedded signers.");
                foreach (SignerInfo signer in signature.SignerInfos)
                {
                    if (signer.Certificate is not { } certificate) continue;
                    var digest = certificate.GetCertHashString(HashAlgorithmName.SHA256);
                    foreach (SignerInfo counter in signer.CounterSignerInfos)
                        foreach (CryptographicAttributeObject attribute in counter.SignedAttributes)
                            if (attribute.Oid.Value == "1.2.840.113549.1.9.5")
                                foreach (AsnEncodedData value in attribute.Values)
                                    signers.Add(new(digest, new DateTimeOffset(new Pkcs9SigningTime(value.RawData).SigningTime.ToUniversalTime())));
                    foreach (CryptographicAttributeObject attribute in signer.UnsignedAttributes)
                        if (attribute.Oid.Value == "1.3.6.1.4.1.311.3.3.1")
                            foreach (AsnEncodedData value in attribute.Values)
                            {
                                var token = new SignedCms(); token.Decode(value.RawData);
                                if (token.ContentInfo.ContentType.Value != "1.2.840.113549.1.9.16.1.4") throw new InvalidDataException("Invalid RFC3161 timestamp content type.");
                                var info = new AsnReader(token.ContentInfo.Content, AsnEncodingRules.DER).ReadSequence();
                                _ = info.ReadInteger(); _ = info.ReadObjectIdentifier(); _ = info.ReadSequence(); _ = info.ReadInteger();
                                signers.Add(new(digest, info.ReadGeneralizedTime()));
                            }
                }
                offset = checked(offset + ((size + 7) & ~7));
            }
            if (signers.Count == 0) return new(timestamp, [], "No publisher certificate and timestamp pair was available.");
            return new(timestamp, signers, null);
        }
        catch (Exception error) when (error is CryptographicException or AsnContentException or InvalidDataException or EndOfStreamException or OverflowException)
        {
            return new(timestamp, [], error.Message);
        }
    }
}
