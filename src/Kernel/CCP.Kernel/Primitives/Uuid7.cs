using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CCP.Kernel.Primitives;

/// <summary>
/// Generates UUID version 7 identifiers (RFC 9562): a 48-bit big-endian Unix
/// millisecond timestamp followed by 74 bits of randomness.
/// <para>
/// Chosen over sequential integers because IDs appear in URLs and sequential
/// IDs leak record counts and permit enumeration. Chosen over UUID v4 because
/// v4's randomness fragments B-tree indexes and destroys insert locality —
/// which matters enormously on the audit table (ADR-004).
/// </para>
/// </summary>
public static class Uuid7
{
    /// <summary>Creates a new time-ordered identifier using the current time.</summary>
    public static Guid NewGuid() => NewGuid(DateTimeOffset.UtcNow);

    /// <summary>Creates a new time-ordered identifier for a specific instant.</summary>
    public static Guid NewGuid(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];

        // Bytes 0-5: 48-bit big-endian Unix timestamp in milliseconds.
        long milliseconds = timestamp.ToUnixTimeMilliseconds();
        Span<byte> timestampBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(timestampBytes, milliseconds);
        timestampBytes[2..8].CopyTo(bytes);

        // Bytes 6-15: random.
        RandomNumberGenerator.Fill(bytes[6..]);

        // Byte 6, high nibble: version 7.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // Byte 8, top two bits: RFC 9562 variant (10xx).
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return CreateGuidFromBigEndianBytes(bytes);
    }

    /// <summary>
    /// Reads the timestamp embedded in a UUID v7. Useful for diagnostics and
    /// for verifying ordering in tests.
    /// </summary>
    public static DateTimeOffset GetTimestamp(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteBigEndianBytes(value, bytes);

        Span<byte> timestampBytes = stackalloc byte[8];
        bytes[..6].CopyTo(timestampBytes[2..]);

        return DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64BigEndian(timestampBytes));
    }

    // Guid's own byte layout is mixed-endian on little-endian machines, so the
    // conversion is done explicitly rather than relying on Guid(byte[]).
    private static Guid CreateGuidFromBigEndianBytes(ReadOnlySpan<byte> bytes)
    {
        int a = BinaryPrimitives.ReadInt32BigEndian(bytes[..4]);
        short b = BinaryPrimitives.ReadInt16BigEndian(bytes[4..6]);
        short c = BinaryPrimitives.ReadInt16BigEndian(bytes[6..8]);

        return new Guid(a, b, c,
            bytes[8], bytes[9], bytes[10], bytes[11],
            bytes[12], bytes[13], bytes[14], bytes[15]);
    }

    private static void WriteBigEndianBytes(Guid value, Span<byte> destination)
    {
        Span<byte> raw = stackalloc byte[16];
        value.TryWriteBytes(raw);

        if (BitConverter.IsLittleEndian)
        {
            // Reverse the first three mixed-endian groups.
            destination[0] = raw[3]; destination[1] = raw[2];
            destination[2] = raw[1]; destination[3] = raw[0];
            destination[4] = raw[5]; destination[5] = raw[4];
            destination[6] = raw[7]; destination[7] = raw[6];
        }
        else
        {
            raw[..8].CopyTo(destination);
        }

        raw[8..].CopyTo(destination[8..]);
    }
}
