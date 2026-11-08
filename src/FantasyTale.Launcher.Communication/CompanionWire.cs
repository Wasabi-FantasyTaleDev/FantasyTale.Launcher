using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyTale.Launcher.Communication;

// Provisional wire v1, isolated from the plugin SDK until server fixtures are agreed.
internal sealed record CompanionEnvelope(
    string Kind, string Id, string Type, int Version, JsonElement Payload,
    string? ReplyTo = null, string? Epoch = null, string? OperationId = null)
{
    internal int ReceivedBytes { get; init; }
}

internal static class CompanionWire
{
    internal const string Subprotocol = "fantasytale.companion.v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConcurrentDictionary<int, JsonSerializerOptions> CachedOptions = new();

    private static JsonSerializerOptions Options(CompanionLimits limits) => CachedOptions.GetOrAdd(limits.JsonDepth, depth => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, MaxDepth = depth,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    });

    internal static JsonElement EncodePayload(object? payload, CompanionLimits limits)
    {
        using var stream = new BoundedMessageStream(limits.MessageBytes);
        JsonSerializer.Serialize(stream, payload, Options(limits));
        using var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = limits.JsonDepth });
        return document.RootElement.Clone();
    }

    internal static byte[] Encode(CompanionEnvelope envelope, CompanionLimits limits)
    {
        Validate(envelope);
        // Serialize through a bounded stream so a plugin cannot allocate an unbounded wire buffer.
        using var stream = new BoundedMessageStream(limits.MessageBytes);
        JsonSerializer.Serialize(stream, envelope, Options(limits));
        return stream.ToArray();
    }

    internal static CompanionEnvelope Decode(byte[] bytes, CompanionLimits limits)
    {
        if (bytes.Length > limits.MessageBytes) throw new IOException("companion.message-too-large");
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.JsonDepth });
            RejectDuplicateProperties(document.RootElement);
            var envelope = document.RootElement.Deserialize<CompanionEnvelope>(Options(limits))
                ?? throw new IOException("companion.invalid-envelope");
            Validate(envelope);
            return envelope with { ReceivedBytes = bytes.Length };
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or ArgumentException)
        {
            // Do not propagate parser exceptions containing remote payload fragments.
            throw new IOException("companion.invalid-envelope");
        }
    }

    internal static async Task<CompanionEnvelope?> ReceiveAsync(WebSocket socket, CompanionLimits limits, CancellationToken token)
    {
        using var message = new BoundedMessageStream(limits.MessageBytes);
        var buffer = new byte[Math.Min(8192, limits.MessageBytes + 1)];
        var fragments = 0;
        do
        {
            if (++fragments > limits.MessageFragments) throw new IOException("companion.fragment-limit");
            var part = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (part.MessageType == WebSocketMessageType.Close) return null;
            if (part.MessageType != WebSocketMessageType.Text) throw new IOException("companion.text-required");
            message.Write(buffer.AsSpan(0, part.Count));
            if (part.EndOfMessage) return Decode(message.ToArray(), limits);
        } while (true);
    }

    internal static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96 || !char.IsAsciiLetterLower(value[0])
            || value.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('.' or '-' or '_')))
            throw new ArgumentException("Invalid companion type identity.");
    }

    internal static void ValidateOwner(string value)
    {
        // Package ids may begin with a digit; owner scopes are local identities, not wire message types.
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96
            || value.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('.' or '-' or '_')))
            throw new ArgumentException("Invalid companion owner identity.");
    }

    private static void Validate(CompanionEnvelope envelope)
    {
        if (envelope.Kind is not ("hello" or "welcome" or "event" or "request" or "response" or "error" or "heartbeat" or "heartbeat_ack" or "state"))
            throw new IOException("companion.invalid-kind");
        ValidateName(envelope.Type);
        if (!ValidId(envelope.Id) || envelope.Version is < 1 or > 65535
            || envelope.Payload.ValueKind == JsonValueKind.Undefined
            || envelope.ReplyTo is not null && !ValidId(envelope.ReplyTo)
            || envelope.Epoch is not null && !ValidId(envelope.Epoch)
            || envelope.OperationId is not null && !ValidId(envelope.OperationId)
            || (envelope.Kind is "response" or "error" or "heartbeat_ack") != (envelope.ReplyTo is not null))
            throw new IOException("companion.invalid-envelope");
    }

    private static bool ValidId(string? id) => id is { Length: > 0 and <= 128 }
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new IOException("companion.duplicate-property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        else if (element.ValueKind == JsonValueKind.String) _ = element.GetString();
    }

    private sealed class BoundedMessageStream(int maximum) : Stream
    {
        private readonly MemoryStream _buffer = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
        public byte[] ToArray() => _buffer.ToArray();
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > maximum) throw new IOException("companion.message-too-large");
            _buffer.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            if (Length == maximum) throw new IOException("companion.message-too-large");
            _buffer.WriteByte(value);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}
