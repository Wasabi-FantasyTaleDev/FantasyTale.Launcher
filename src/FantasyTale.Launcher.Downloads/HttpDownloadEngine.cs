using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Downloads;

/// <summary>Streams bounded files to hash-specific partials, validates ranged replies and publishes only verified bytes.</summary>
public sealed class HttpDownloadEngine(HttpClient http) : IDownloadEngine
{
    /// <summary>Identifies the built-in HTTP transport independently of its source adapter.</summary>
    public string Name => "http";

    /// <summary>Resumes an interrupted file; a server ignoring Range restarts it. Failed integrity never replaces the destination.</summary>
    public async Task DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var routing = new HttpDownloadRouting();
        while (true)
        {
            try
            {
                await DownloadAttemptAsync(request, progress, routing, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && routing.HasNext &&
                error is HttpRequestException or InvalidDataException or OperationCanceledException)
            {
                // Network interruptions retain the partial; invalid content is
                // discarded by the attempt before trying the next origin.
            }
        }
    }

    private async Task DownloadAttemptAsync(DownloadRequest request, IProgress<DownloadProgress>? progress,
        HttpDownloadRouting routing, CancellationToken cancellationToken)
    {
        if (request.Size < 0 || request.Sha256.Length != 64 || request.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid download size or SHA-256.");
        if (request.Source.Url.Scheme is not ("http" or "https") || request.Source.Url.UserInfo.Length != 0 || request.Source.Size is { } size && size != request.Size)
            throw new InvalidDataException("Source metadata does not match the release.");
        var destination = Path.GetFullPath(request.Destination);
        var partial = destination + "." + request.Sha256.ToLowerInvariant() + ".part";
        RejectLinks(destination); RejectLinks(partial);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (await MatchesAsync(destination, request.Size, request.Sha256, cancellationToken).ConfigureAwait(false))
        { progress?.Report(new(request.Size, request.Size, 0)); return; }
        try
        {
            await using (var file = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (file.Length > request.Size) file.SetLength(0);
                var offset = file.Length;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await HashPrefixAsync(file, offset, hash, cancellationToken).ConfigureAwait(false);
                if (offset < request.Size)
                {
                    using var message = new HttpRequestMessage(HttpMethod.Get, request.Source.Url);
                    message.Options.Set(HttpDownloadRouting.Key, routing);
                    foreach (var header in request.Source.Headers)
                    {
                        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) message.Headers.Host = header.Value;
                        else if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)) message.Headers.Add(header.Key, header.Value);
                        else throw new InvalidDataException("Unsupported storage request header.");
                    }
                    if (offset > 0) message.Headers.Range = new RangeHeaderValue(offset, null);
                    using var activity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    routing.HeaderDeadline = activity;
                    activity.CancelAfter(routing.HeaderTimeout);
                    using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, activity.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        var range = response.Content.Headers.ContentRange;
                        if (range is null || range.Unit != "bytes" || range.From != offset || range.To != request.Size - 1 || range.Length != request.Size)
                            throw new InvalidDataException("Invalid Content-Range from storage.");
                    }
                    else if (response.StatusCode == HttpStatusCode.OK) { offset = 0; file.SetLength(0); _ = hash.GetHashAndReset(); }
                    else throw new InvalidDataException("Unexpected storage response status.");
                    if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength is { } length && length != request.Size - offset)
                        throw new InvalidDataException("Storage response length or encoding does not match the release.");
                    file.Position = offset;
                    await using var input = await response.Content.ReadAsStreamAsync(activity.Token).ConfigureAwait(false);
                    var buffer = new byte[131072]; var clock = Stopwatch.StartNew(); long sampled = offset;
                    progress?.Report(new(offset, request.Size, 0));
                    while (true)
                    {
                        activity.CancelAfter(routing.HeaderTimeout);
                        int count;
                        try { count = await input.ReadAsync(buffer, activity.Token).ConfigureAwait(false); }
                        catch (IOException error) { throw new HttpRequestException("The storage connection was interrupted.", error); }
                        if (count == 0) break;
                        if (count > request.Size - file.Position) throw new InvalidDataException("Storage sent more bytes than declared.");
                        await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        hash.AppendData(buffer, 0, count);
                        if (clock.ElapsedMilliseconds >= 250)
                        {
                            progress?.Report(new(file.Position, request.Size, (file.Position - sampled) / clock.Elapsed.TotalSeconds));
                            sampled = file.Position; clock.Restart();
                        }
                    }
                    if (file.Length != request.Size) throw new HttpRequestException("The transfer ended before the declared file length.");
                }
                progress?.Report(new(request.Size, request.Size, 0));
                var digest = hash.GetHashAndReset();
                if (!Convert.ToHexString(digest).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Downloaded file failed SHA-256 verification.");
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(destination); File.Move(partial, destination, overwrite: true);
        }
        catch (InvalidDataException) { File.Delete(partial); throw; }
    }

    private static async Task HashPrefixAsync(FileStream file, long length, IncrementalHash hash, CancellationToken cancellationToken)
    {
        file.Position = 0;
        if (length == 0) return;
        var buffer = new byte[131072];
        long remaining = length;
        while (remaining > 0)
        {
            var count = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new InvalidDataException("The partial download ended before its recorded length.");
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }
    }

    /// <summary>Verifies existing content without reading an unbounded file into memory.</summary>
    public static async Task<bool> MatchesAsync(string path, long size, string sha256, CancellationToken cancellationToken)
    {
        RejectLinks(path);
        if (!File.Exists(path)) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return file.Length == size && Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Download paths cannot contain symbolic links or reparse points.");
    }
}
