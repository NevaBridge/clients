using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace NevaBridge.Chat;

/// <summary>One recognized stretch of speech as Amazon Transcribe streaming reports it.</summary>
/// <param name="ResultId">Identifies the result; a final result replaces the partial one with its id.</param>
/// <param name="Text">The recognized words.</param>
/// <param name="IsPartial">True while Transcribe may still revise this result.</param>
public sealed record TranscriptResult(string ResultId, string Text, bool IsPartial)
{
    /// <summary>Which recognition session reported it, counting from 0; later sessions follow earlier ones.</summary>
    public int Session { get; init; }
}

/// <summary>What one message from the Transcribe stream means for the listener.</summary>
internal abstract record TranscribeMessage;

internal sealed record TranscribeResults(IReadOnlyList<TranscriptResult> Results) : TranscribeMessage
{
    public bool Equals(TranscribeResults? other) => other is not null && Results.SequenceEqual(other.Results);

    public override int GetHashCode() => Results.Count;
}

internal sealed record TranscribeFailure(string Type, string Message) : TranscribeMessage;

/// <summary>A decoded event-stream message: its string headers and its payload.</summary>
internal sealed record EventStreamMessage(IReadOnlyDictionary<string, string> Headers, byte[] Payload);

/// <summary>
/// The AWS event-stream framing Amazon Transcribe streaming speaks over its WebSocket: a prelude
/// with both lengths and its CRC32, the headers, the payload and a CRC32 of the whole message.
/// </summary>
internal static class EventStreamCodec
{
    private const int PreludeLength = 12;
    private const int ChecksumLength = 4;
    private const byte StringHeader = 7;

    private static readonly (string Name, string Value)[] AudioEventHeaders =
    [
        (":message-type", "event"),
        (":event-type", "AudioEvent"),
        (":content-type", "application/octet-stream"),
    ];

    /// <summary>Frames 16-bit PCM audio as an <c>AudioEvent</c>; empty audio ends the stream.</summary>
    public static byte[] EncodeAudioEvent(ReadOnlySpan<byte> pcm)
    {
        int headersLength = 0;
        foreach ((string name, string value) in AudioEventHeaders)
        {
            headersLength += 1 + Encoding.UTF8.GetByteCount(name) + 1 + 2 + Encoding.UTF8.GetByteCount(value);
        }

        int totalLength = PreludeLength + headersLength + pcm.Length + ChecksumLength;
        byte[] frame = new byte[totalLength];
        Span<byte> span = frame;
        BinaryPrimitives.WriteInt32BigEndian(span, totalLength);
        BinaryPrimitives.WriteInt32BigEndian(span[4..], headersLength);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], Crc32.Compute(span[..8]));

        int offset = PreludeLength;
        foreach ((string name, string value) in AudioEventHeaders)
        {
            int nameLength = Encoding.UTF8.GetBytes(name, span[(offset + 1)..]);
            span[offset] = (byte)nameLength;
            offset += 1 + nameLength;
            span[offset++] = StringHeader;
            int valueLength = Encoding.UTF8.GetBytes(value, span[(offset + 2)..]);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], (ushort)valueLength);
            offset += 2 + valueLength;
        }

        pcm.CopyTo(span[offset..]);
        offset += pcm.Length;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Crc32.Compute(span[..offset]));
        return frame;
    }

    /// <summary>Decodes one message, checking both checksums.</summary>
    /// <exception cref="InvalidDataException">The bytes are not one intact event-stream message.</exception>
    public static EventStreamMessage Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < PreludeLength + ChecksumLength)
        {
            throw new InvalidDataException("The event-stream message is shorter than its prelude.");
        }

        int totalLength = BinaryPrimitives.ReadInt32BigEndian(frame);
        int headersLength = BinaryPrimitives.ReadInt32BigEndian(frame[4..]);
        if (BinaryPrimitives.ReadUInt32BigEndian(frame[8..]) != Crc32.Compute(frame[..8]))
        {
            throw new InvalidDataException("The event-stream prelude checksum does not match.");
        }

        if (totalLength != frame.Length || headersLength < 0 || PreludeLength + headersLength + ChecksumLength > totalLength)
        {
            throw new InvalidDataException("The event-stream message lengths do not match its size.");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(frame[^ChecksumLength..]) != Crc32.Compute(frame[..^ChecksumLength]))
        {
            throw new InvalidDataException("The event-stream message checksum does not match.");
        }

        Dictionary<string, string> headers = ReadHeaders(frame.Slice(PreludeLength, headersLength));
        byte[] payload = frame[(PreludeLength + headersLength)..^ChecksumLength].ToArray();
        return new EventStreamMessage(headers, payload);
    }

    /// <summary>
    /// Reads one message from the Transcribe stream: its results, its exception, or null for a
    /// message the listener does not need.
    /// </summary>
    /// <exception cref="InvalidDataException">The message is damaged or its body is not JSON.</exception>
    public static TranscribeMessage? DecodeTranscribeMessage(ReadOnlySpan<byte> frame)
    {
        EventStreamMessage message = Decode(frame);
        string? messageType = message.Headers.GetValueOrDefault(":message-type");
        if (messageType is "exception" or "error")
        {
            JsonElement detail = ParseBody(message.Payload);
            string type = message.Headers.GetValueOrDefault(":exception-type")
                ?? message.Headers.GetValueOrDefault(":error-code")
                ?? "UnknownError";
            return new TranscribeFailure(type, ReadString(detail, "Message") ?? ReadString(detail, "message") ?? string.Empty);
        }

        if (message.Headers.GetValueOrDefault(":event-type") != "TranscriptEvent")
        {
            return null;
        }

        JsonElement body = ParseBody(message.Payload);

        List<TranscriptResult> results = [];
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("Transcript", out JsonElement transcript)
            && transcript.ValueKind == JsonValueKind.Object
            && transcript.TryGetProperty("Results", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                string text = FirstAlternative(item);
                if (text.Length > 0)
                {
                    bool partial = item.TryGetProperty("IsPartial", out JsonElement isPartial) && isPartial.ValueKind == JsonValueKind.True;
                    results.Add(new TranscriptResult(ReadString(item, "ResultId") ?? string.Empty, text, partial));
                }
            }
        }

        return new TranscribeResults(results);
    }

    private static Dictionary<string, string> ReadHeaders(ReadOnlySpan<byte> bytes)
    {
        Dictionary<string, string> headers = [];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int nameLength = bytes[offset++];
            string name = Encoding.UTF8.GetString(Take(bytes, ref offset, nameLength));
            byte type = Take(bytes, ref offset, 1)[0];
            int valueLength = type switch
            {
                0 or 1 => 0,
                2 => 1,
                3 => 2,
                4 => 4,
                5 or 8 => 8,
                9 => 16,
                6 or 7 => BinaryPrimitives.ReadUInt16BigEndian(Take(bytes, ref offset, 2)),
                _ => throw new InvalidDataException($"The event-stream header type {type} is unknown."),
            };
            ReadOnlySpan<byte> value = Take(bytes, ref offset, valueLength);
            if (type == StringHeader)
            {
                headers[name] = Encoding.UTF8.GetString(value);
            }
        }

        return headers;
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> bytes, ref int offset, int length)
    {
        if (offset + length > bytes.Length)
        {
            throw new InvalidDataException("An event-stream header runs past the headers.");
        }

        ReadOnlySpan<byte> taken = bytes.Slice(offset, length);
        offset += length;
        return taken;
    }

    private static JsonElement ParseBody(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The Transcribe message body is not JSON.", error);
        }
    }

    private static string FirstAlternative(JsonElement result) =>
        result.TryGetProperty("Alternatives", out JsonElement alternatives)
        && alternatives.ValueKind == JsonValueKind.Array
        && alternatives.GetArrayLength() > 0
            ? ReadString(alternatives[0], "Transcript") ?? string.Empty
            : string.Empty;

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>CRC-32 (IEEE 802.3, the zlib polynomial), which event-stream checksums use.</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
